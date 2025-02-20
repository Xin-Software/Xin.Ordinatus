namespace Xin.Ordinatus;

public class AsyncFunnel
{
    private readonly SemaphoreSlim semaphore;

    public AsyncFunnel(int maxConcurrentTasks)
    {
        this.semaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
    }

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
