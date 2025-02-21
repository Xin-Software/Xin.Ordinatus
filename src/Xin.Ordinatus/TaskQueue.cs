using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Xin.Ordinatus.Utilities;

namespace Xin.Ordinatus;

/// <summary>
/// Represents the options for the <see cref="TaskQueue"/> class.
/// </summary>
public record TaskQueueOptions
{
    /// <summary>
    /// Gets or sets the maximum number of parallel tasks allowed.
    /// </summary>
    public int MaxParallelTasks { get; init; } = 4;

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the options are valid.</returns>
    public ValidationResult Validate()
    {
        var errors = new List<ValidationFailure>();

        if (this.MaxParallelTasks <= 0)
        {
            errors.Add(new ValidationFailure
            {
                PropertyName = nameof(this.MaxParallelTasks),
                ErrorMessage = "Maximum parallel tasks must be greater than 0."
            });
        }

        return new ValidationResult(errors);
    }
}

/// <summary>
/// Provides a task queue with a specified maximum number of parallel tasks.
/// </summary>
public class TaskQueue : IAsyncDisposable
{
    private readonly ILogger logger;
    private readonly AsyncManualResetEvent runSignal = new AsyncManualResetEvent(false);
    private readonly SemaphoreSlim concurrencySempahore;
    private readonly Channel<Func<CancellationToken, Task>> channel;
    private readonly ConcurrentBag<Task> runningTasks = new();

    private CancellationTokenSource? cts;
    private int isRunning = 0;
    private Task? processLoopTask;

    /// <summary>
    /// Occurs when an error is encountered during task processing.
    /// </summary>
    public event Action<Exception>? OnError;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskQueue"/> class.
    /// </summary>
    /// <param name="logger">The logger to use for logging information and errors.</param>
    /// <param name="options">The <see cref="TaskQueueOptions">options</see> for the task queue.</param>
    /// <exception cref="ArgumentException">Thrown when the <paramref name="options"/> are invalid.</exception>
    public TaskQueue(ILogger<TaskQueue> logger, TaskQueueOptions options)
    {
        this.logger = logger;

        var validationResult = options.Validate();

        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                this.logger.LogError("Error initializing TaskQueue: {ErrorMessage}", error.ErrorMessage);
            }
            throw new ArgumentException("Invalid TaskQueue options.", nameof(options));
        }

        this.concurrencySempahore = new SemaphoreSlim(options.MaxParallelTasks, options.MaxParallelTasks);
        this.channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>();
    }

    /// <summary>
    /// Enqueues a task to be executed, ensuring that the maximum number of parallel tasks is not exceeded.
    /// </summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task.</param>
    /// <returns>A task that represents the enqueued task.</returns>
    /// <exception cref="Exceptions.QueueStoppedException">Thrown when the queue is stopped.</exception>
    public async Task Enqueue(Func<CancellationToken, Task> task, CancellationToken cancellationToken = default)
    {
        try
        {
            await this.channel.Writer.WriteAsync(task, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            throw new Exceptions.QueueStoppedException();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error enqueuing task.");
            this.OnError?.Invoke(ex);
        }
    }

    /// <summary>
    /// Starts processing tasks in the queue.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the task processing.</param>
    public void Start(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref this.isRunning, 1) == 1)
        {
            this.logger.LogWarning("Queue is already running.");
            return;
        }

        this.cts = new CancellationTokenSource();

        var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.cts.Token).Token;

        this.processLoopTask = Task.Run(() => this.ProcessLoop(linkedToken), linkedToken);
        this.Resume();
        this.logger.LogInformation("Queue started.");
    }

    /// <summary>
    /// Pauses the task processing in the queue.
    /// </summary>
    public void Pause()
    {
        if (this.isRunning == 0)
        {
            this.logger.LogWarning("Cannot pause, queue is not running.");
            return;
        }

        this.logger.LogInformation("Pausing the queue...");
        this.runSignal.Reset();
    }

    /// <summary>
    /// Resumes the task processing in the queue.
    /// </summary>
    public void Resume()
    {
        if (this.isRunning == 0)
        {
            this.logger.LogWarning("Cannot resume, queue is not running.");
            return;
        }

        this.logger.LogInformation("Resuming the queue...");
        this.runSignal.Set();
    }

    /// <summary>
    /// Stops the task processing in the queue asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous stop operation.</returns>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref this.isRunning, 0) == 0)
        {
            return;
        }

        this.logger.LogInformation("Stopping the queue...");

        this.cts?.Cancel();
        this.runSignal.Reset();
        this.channel.Writer.Complete();

        if (this.processLoopTask is not null)
        {
            this.logger.LogInformation("Waiting for process loop to complete...");
            await this.processLoopTask;
            this.logger.LogInformation("Process loop stopped.");
        }

        this.logger.LogInformation("Waiting for running tasks to complete...");
        await Task.WhenAll(this.runningTasks.ToArray());
        this.logger.LogInformation("All running tasks completed.");

        this.logger.LogInformation("Queue stopped.");
    }

    /// <summary>
    /// Processes a single task from the queue.
    /// </summary>
    /// <param name="task">The task to process.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task processing.</param>
    private async Task ProcessQueueItem(Func<CancellationToken, Task> task, CancellationToken cancellationToken)
    {
        string taskType = task.GetType().Name;

        try
        {
            await this.concurrencySempahore.WaitAsync(cancellationToken);

            try
            {
                await this.runSignal.WaitAsync(cancellationToken);

                this.logger.LogDebug("Processing task: {taskType}", taskType);
                await task(cancellationToken);
            }
            finally
            {
                this.concurrencySempahore.Release();
            }

        }
        catch (OperationCanceledException)
        {
            // Ignore
            this.logger.LogInformation("Task cancelled: {taskType}", taskType);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error processing task: {taskType}", taskType);

            this.OnError?.Invoke(ex);
        }
    }

    /// <summary>
    /// Processes tasks in the queue in a loop.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the task processing.</param>
    private async Task ProcessLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await this.runSignal.WaitAsync(cancellationToken);

                var task = await this.channel.Reader.ReadAsync(cancellationToken);
                var runningTask = this.ProcessQueueItem(task, cancellationToken);
                this.runningTasks.Add(runningTask);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                runningTask.ContinueWith(_ => this.runningTasks.TryTake(out var _));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }
            catch (ChannelClosedException)
            {
                this.logger.LogInformation("Channel closed, exiting process loop.");
                break;
            }
            catch (OperationCanceledException)
            {
                this.logger.LogInformation("Loop cancelled, exiting process loop.");
                break;
            }
        }

        Interlocked.Exchange(ref this.isRunning, 0);
    }

    /// <summary>
    /// Disposes the <see cref="TaskQueue"/> asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        this.concurrencySempahore.Dispose();
        await this.runSignal.DisposeAsync();
    }
}
