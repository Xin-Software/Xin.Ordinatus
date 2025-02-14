using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Xin.Ordinatus;

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

    public event Action<Exception>? OnError;

    public TaskQueue(ILogger<TaskQueue> logger, int maxParallelTasks = 4)
    {
        this.logger = logger;

        if (maxParallelTasks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxParallelTasks), "Maximum parallel tasks must be greater than 0.");
        }

        this.concurrencySempahore = new SemaphoreSlim(maxParallelTasks, maxParallelTasks);
        this.channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>();
    }

    public async Task Enqueue(Func<CancellationToken, Task> task)
    {
        try
        {
            await this.channel.Writer.WriteAsync(task);
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

    public async ValueTask DisposeAsync()
    {
        this.concurrencySempahore.Dispose();
        await this.runSignal.DisposeAsync();
    }
}
