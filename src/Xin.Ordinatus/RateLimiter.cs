using System.Diagnostics;
using System.Threading.Channels;

namespace Xin.Ordinatus;

/// <summary>
/// Provides a mechanism to limit the rate at which tasks are executed.
/// </summary>
public class RateLimiter : IAsyncDisposable
{
    private readonly TimeSpan period;
    private readonly Channel<Func<CancellationToken, Task>> channel;
    private readonly CancellationTokenSource cts;
    private readonly Task processLoopTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimiter"/> class.
    /// </summary>
    /// <param name="period">The time period to wait between task executions.</param>
    public RateLimiter(TimeSpan period)
    {
        this.period = period;
        this.channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>();
        this.cts = new CancellationTokenSource();
        this.processLoopTask = Task.Run(this.ProcessLoopAsync);
    }

    /// <summary>
    /// Disposes the <see cref="RateLimiter"/> asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        this.cts.Cancel();
        this.channel.Writer.Complete();

        try
        {
            await Task.WhenAny(this.processLoopTask, Task.Delay(100));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException))
        {
            // Ignore
        }

        this.cts.Dispose();
    }

    /// <summary>
    /// Enqueues a task to be executed, ensuring that the rate limit is respected.
    /// </summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task.</param>
    /// <returns>A task that represents the enqueued task.</returns>
    /// <remarks>
    /// If the rate limit is reached, the task will be delayed until the rate limit allows it to be executed.
    /// </remarks>
    public Task Enqueue(Func<Task> task, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, Task> wrappedTask = async ct =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);

            if (linkedCts.Token.IsCancellationRequested)
            {
                tcs.TrySetCanceled();
                return;
            }

            try
            {
                await task();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        };

        this.channel.Writer.TryWrite(wrappedTask);
        return tcs.Task;
    }

    /// <summary>
    /// Enqueues a task to be executed, ensuring that the rate limit is respected.
    /// </summary>
    /// <param name="task">The task to execute, which accepts a cancellation token.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task.</param>
    /// <returns>A task that represents the enqueued task.</returns>
    /// <remarks>
    /// If the rate limit is reached, the task will be delayed until the rate limit allows it to be executed.
    /// </remarks>
    public Task Enqueue(Func<CancellationToken, Task> task, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, Task> wrappedTask = async ct =>
        {
            Debug.WriteLine("Executing wrapped queue task");
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);

            try
            {
                await task(linkedCts.Token);
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error executing task");
                tcs.SetException(ex);
            }
        };

        this.channel.Writer.TryWrite(wrappedTask);
        return tcs.Task;
    }

    /// <summary>
    /// Enqueues a task to be executed, ensuring that the rate limit is respected.
    /// </summary>
    /// <typeparam name="TInput">The type of the input parameter.</typeparam>
    /// <param name="input">The input parameter for the task.</param>
    /// <param name="task">The task to execute.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task.</param>
    /// <returns>A task that represents the enqueued task.</returns>
    /// <remarks>
    /// If the rate limit is reached, the task will be delayed until the rate limit allows it to be executed.
    /// </remarks>
    public Task Enqueue<TInput>(TInput input, Func<TInput, Task> task, CancellationToken cancellationToken = default)
    {
        return this.Enqueue(() => task(input), cancellationToken);
    }

    /// <summary>
    /// Enqueues a task to be executed, ensuring that the rate limit is respected.
    /// </summary>
    /// <typeparam name="TInput">The type of the input parameter.</typeparam>
    /// <param name="input">The input parameter for the task.</param>
    /// <param name="task">The task to execute, which accepts a cancellation token.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task.</param>
    /// <returns>A task that represents the enqueued task.</returns>
    /// <remarks>
    /// If the rate limit is reached, the task will be delayed until the rate limit allows it to be executed.
    /// </remarks>
    public Task Enqueue<TInput>(TInput input, Func<TInput, CancellationToken, Task> task, CancellationToken cancellationToken = default)
    {
        return this.Enqueue((ct) => task(input, ct), cancellationToken);
    }

    /// <summary>
    /// Enqueues a task to be executed, ensuring that the rate limit is respected.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the task.</typeparam>
    /// <param name="task">The task to execute.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task.</param>
    /// <returns>A task that represents the enqueued task, with a result of type <typeparamref name="T"/>.</returns>
    /// <remarks>
    /// If the rate limit is reached, the task will be delayed until the rate limit allows it to be executed.
    /// </remarks>
    public Task<T> Enqueue<T>(Func<Task<T>> task, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, Task> wrappedTask = async ct =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);

            if (linkedCts.Token.IsCancellationRequested)
            {
                tcs.TrySetCanceled();
                return;
            }

            try
            {
                var result = await task();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        };

        this.channel.Writer.TryWrite(wrappedTask);
        return tcs.Task;
    }

    /// <summary>
    /// Enqueues a task to be executed, ensuring that the rate limit is respected.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the task.</typeparam>
    /// <param name="task">The task to execute, which accepts a cancellation token.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task.</param>
    /// <returns>A task that represents the enqueued task, with a result of type <typeparamref name="T"/>.</returns>
    /// <remarks>
    /// If the rate limit is reached, the task will be delayed until the rate limit allows it to be executed.
    /// </remarks>
    public Task<T> Enqueue<T>(Func<CancellationToken, Task<T>> task, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, Task> wrappedTask = async ct =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);

            try
            {
                var result = await task(linkedCts.Token);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        };

        this.channel.Writer.TryWrite(wrappedTask);
        return tcs.Task;
    }

    /// <summary>
    /// Enqueues a task to be executed, ensuring that the rate limit is respected.
    /// </summary>
    /// <typeparam name="TInput">The type of the input parameter.</typeparam>
    /// <typeparam name="T">The type of the result returned by the task.</typeparam>
    /// <param name="input">The input parameter for the task.</param>
    /// <param name="task">The task to execute.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task.</param>
    /// <returns>A task that represents the enqueued task, with a result of type <typeparamref name="T"/>.</returns>
    /// <remarks>
    /// If the rate limit is reached, the task will be delayed until the rate limit allows it to be executed.
    /// </remarks>
    public Task<T> Enqueue<TInput, T>(TInput input, Func<TInput, Task<T>> task, CancellationToken cancellationToken = default)
    {
        return this.Enqueue(() => task(input), cancellationToken);
    }

    /// <summary>
    /// Enqueues a task to be executed, ensuring that the rate limit is respected.
    /// </summary>
    /// <typeparam name="TInput">The type of the input parameter.</typeparam>
    /// <typeparam name="T">The type of the result returned by the task.</typeparam>
    /// <param name="input">The input parameter for the task.</param>
    /// <param name="task">The task to execute, which accepts a cancellation token.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the task.</param>
    /// <returns>A task that represents the enqueued task, with a result of type <typeparamref name="T"/>.</returns>
    /// <remarks>
    /// If the rate limit is reached, the task will be delayed until the rate limit allows it to be executed.
    /// </remarks>
    public Task<T> Enqueue<TInput, T>(TInput input, Func<TInput, CancellationToken, Task<T>> task, CancellationToken cancellationToken = default)
    {
        return this.Enqueue((ct) => task(input, ct), cancellationToken);
    }

    /// <summary>
    /// Processes the tasks in the channel, ensuring that the rate limit is respected.
    /// </summary>
    /// <returns>A task that represents the processing loop.</returns>
    private async Task ProcessLoopAsync()
    {
        while (await this.channel.Reader.WaitToReadAsync(this.cts.Token))
        {
            var task = await this.channel.Reader.ReadAsync(this.cts.Token);

            var startTime = Stopwatch.GetTimestamp();
            await task(this.cts.Token);
            var elapsed = Stopwatch.GetElapsedTime(startTime);

            var delay = this.period - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, this.cts.Token);
            }
        }
    }
}
