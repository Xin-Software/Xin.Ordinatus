namespace Xin.Ordinatus;

/// <summary>
/// Controls the number of concurrent asynchronous tasks.
/// </summary>
public class AsyncFunnel
{
    private readonly SemaphoreSlim semaphore;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncFunnel"/> class.
    /// </summary>
    /// <param name="maxConcurrentTasks">The maximum number of concurrent tasks allowed.</param>
    public AsyncFunnel(int maxConcurrentTasks)
    {
        this.semaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
    }

    /// <summary>
    /// Runs the specified asynchronous function, ensuring that the number of concurrent tasks does not exceed the limit.
    /// </summary>
    /// <param name="func">The asynchronous function to run.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// If the maximum number of concurrent tasks is already running, this method will wait until one of the tasks completes.
    /// </remarks>
    public async Task RunAsync(Func<Task> func)
    {
        await this.semaphore.WaitAsync();
        try
        {
            await func();
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    /// <summary>
    /// Runs the specified asynchronous function, ensuring that the number of concurrent tasks does not exceed the limit.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the asynchronous function.</typeparam>
    /// <param name="func">The asynchronous function to run.</param>
    /// <returns>A task representing the asynchronous operation, with a result of type <typeparamref name="T"/>.</returns>
    /// <remarks>
    /// If the maximum number of concurrent tasks is already running, this method will wait until one of the tasks completes.
    /// </remarks>
    public async Task<T> RunAsync<T>(Func<Task<T>> func)
    {
        await this.semaphore.WaitAsync();
        try
        {
            return await func();
        }
        finally
        {
            this.semaphore.Release();
        }
    }
}
