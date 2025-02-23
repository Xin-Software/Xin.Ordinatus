using System.Diagnostics;
using Xunit.Sdk;

namespace Xin.Ordinatus.Tests;

public class RateLimiterTests
{
    [Fact]
    public async Task Enqueue_TasksAreExecuted()
    {
        // Arrange
        var rateLimiter = new RateLimiter(period: TimeSpan.FromSeconds(1));
        int counter = 0;
        Task SimpleTask()
        {
            Interlocked.Increment(ref counter);
            return Task.CompletedTask;
        }

        // Act
        var tasks = Enumerable.Range(0, 5)
                              .Select(_ => rateLimiter.Enqueue(SimpleTask))
                              .ToArray();
        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(5, counter);

        await rateLimiter.DisposeAsync();
    }

    [Fact]
    public async Task Enqueue_RespectsLimit()
    {
        var rateLimiter = new RateLimiter(TimeSpan.FromMilliseconds(100));
        int completedTasks = 0;

        Task SimpleTask()
        {
            Interlocked.Increment(ref completedTasks);
            return Task.CompletedTask;
        };

        var startTime = Stopwatch.GetTimestamp();

        var tasks = Enumerable.Range(0, 10)
                              .Select(_ => rateLimiter.Enqueue(SimpleTask))
                              .ToArray();
        await Task.WhenAll(tasks);
        var elapsed = Stopwatch.GetElapsedTime(startTime).Milliseconds;

        Assert.Equal(10, completedTasks);
        Assert.InRange(elapsed, 900, 1200);
    }

    [Fact]
    public async Task Enqueue_PassesInput()
    {
        var rateLimiter = new RateLimiter(TimeSpan.FromMilliseconds(100));
        var result = 0;

        Task TaskWithInput(int input)
        {
            result = input;
            return Task.CompletedTask;
        };

        await rateLimiter.Enqueue(42, TaskWithInput);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Enqueue_SurfacesException()
    {
        var rateLimiter = new RateLimiter(TimeSpan.FromMilliseconds(100));
        var expectedException = new Exception("Test exception");

        Task TaskWithError()
        {
            throw expectedException;
        }

        var thrownException = await Assert.ThrowsAsync<Exception>(() => rateLimiter.Enqueue(TaskWithError));

        Assert.Same(expectedException, thrownException);
    }

    [Fact]
    public async Task Enqueue_PendingTasksCancelledExternally()
    {
        var rateLimiter = new RateLimiter(TimeSpan.FromMilliseconds(100));
        int completedTasks = 0;

        var cts = new CancellationTokenSource();

        Task SimpleTask()
        {
            Interlocked.Increment(ref completedTasks);
            return Task.CompletedTask;
        };

        await Task.WhenAny(
            Task.WhenAll(Enumerable.Range(0, 10).Select(_ => rateLimiter.Enqueue(SimpleTask, cts.Token))),
            Task.Run(async () =>
            {
                await Task.Delay(50);
                cts.Cancel();
            }));

        Assert.Equal(1, completedTasks);
    }

    [Fact]
    public async Task Enqueue_PendingTasksCancelledInternally()
    {
        var rateLimiter = new RateLimiter(TimeSpan.FromMilliseconds(100));
        int completedTasks = 0;
        int cancelledTasks = 0;

        var cts = new CancellationTokenSource();

        Func<CancellationToken, Task> task = async (ct) =>
        {
            try
            {
                await Task.Delay(100, ct);
                Interlocked.Increment(ref completedTasks);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancelledTasks);
            }
        };

        async Task CancellableTask(CancellationToken ct)
        {
            try
            {
                await Task.Delay(100, ct);
                Interlocked.Increment(ref completedTasks);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancelledTasks);
            }
        };

        await Task.WhenAny(
            Task.WhenAll(Enumerable.Range(0, 10).Select(_ => rateLimiter.Enqueue(CancellableTask, cts.Token))),
            Task.Run(async () =>
            {
                await Task.Delay(110);
                cts.Cancel();
            }));

        await Task.Delay(1000);

        Assert.Equal(1, completedTasks);
        Assert.Equal(9, cancelledTasks);
    }
}
