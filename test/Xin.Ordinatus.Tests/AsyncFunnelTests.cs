namespace Xin.Ordinatus.Tests;

public class AsyncFunnelTests
{
    [Fact]
    public async Task RunAsync_ExecutesTask()
    {
        bool executed = false;
        var funnel = new AsyncFunnel(1);

        await funnel.RunAsync(async () =>
        {
            await Task.Delay(10);
            executed = true;
        });

        Assert.True(executed);
    }

    [Fact]
    public async Task RunAsync_LimitsConcurrency()
    {
        const int maxConcurrentTasks = 2;
        var funnel = new AsyncFunnel(maxConcurrentTasks);
        int currentConcurrency = 0;
        int maxConcurrencyObserved = 0;
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(funnel.RunAsync(async () =>
            {
                int concurrent = Interlocked.Increment(ref currentConcurrency);
                // Record the highest number of concurrent executions
                maxConcurrencyObserved = Math.Max(maxConcurrencyObserved, concurrent);
                await Task.Delay(50); // simulate work
                Interlocked.Decrement(ref currentConcurrency);
            }));
        }
        await Task.WhenAll(tasks);

        Assert.True(maxConcurrencyObserved <= maxConcurrentTasks,
            $"Observed concurrency {maxConcurrencyObserved} exceeded the limit of {maxConcurrentTasks}.");
    }

    [Fact]
    public async Task RunAsync_WithResult_ReturnsExpectedValue()
    {
        var funnel = new AsyncFunnel(1);
        int expectedValue = 42;

        int result = await funnel.RunAsync(async () =>
        {
            await Task.Delay(10);
            return expectedValue;
        });

        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public async Task RunAsync_RaisesException()
    {
        var funnel = new AsyncFunnel(1);
        var expectedException = new InvalidOperationException("test");
        
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            funnel.RunAsync(async () =>
            {
                await Task.Delay(10);
                throw expectedException;
            }));
        
        Assert.Same(expectedException, actualException);
    }
}
