using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Xin.Ordinatus.Tests;

public class TaskQueueTests : IAsyncLifetime
{
    private readonly TaskQueue queue;

    public TaskQueueTests()
    {
        this.queue = new TaskQueue(
            NullLogger<TaskQueue>.Instance,
            maxParallelTasks: 2);
    }

    public Task InitializeAsync()
    {
        // Start the task queue without any external cancellation token
        this.queue.Start(CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Stop & dispose the task queue
        await this.queue.StopAsync();
        await this.queue.DisposeAsync();
    }

    [Fact]
    public async Task Enqueued_ExecutesSuccessfully()
    {
        bool taskExecuted = false;
        var taskCompletionSource = new TaskCompletionSource<bool>();

        await this.queue.Enqueue(async (ct) =>
        {
            await Task.Delay(10, ct);
            taskExecuted = true;
            taskCompletionSource.SetResult(true);
        });

        await taskCompletionSource.Task;

        Assert.True(taskExecuted, "The enqueued task did not execute.");
    }

    [Fact]
    public async Task Enqueue_ExceptionIsHandled()
    {
        Exception? capturedException = null;
        this.queue.OnError += (ex) => capturedException = ex;

        await this.queue.Enqueue(async (ct) =>
        {
            await Task.Delay(10, ct);
            throw new InvalidOperationException("Test exception");
        });

        await Task.Delay(50);

        Assert.NotNull(capturedException);
        Assert.IsType<InvalidOperationException>(capturedException);
    }

    [Fact]
    public async Task PauseAndResume_QueueWorksCorrectly()
    {
        bool taskExecuted = false;
        var taskCompletionSource = new TaskCompletionSource<bool>();

        this.queue.Pause();

        await this.queue.Enqueue(async (ct) =>
        {
            await Task.Delay(10, ct);
            taskExecuted = true;
            taskCompletionSource.SetResult(true);
        });

        await Task.Delay(50);
        Assert.False(taskExecuted, "Task should not execute while the queue is paused.");

        // Resume the queue.
        this.queue.Resume();

        // Wait for the task to complete.
        await taskCompletionSource.Task;
        Assert.True(taskExecuted, "Task should execute after the queue is resumed.");
    }

    [Fact]
    public async Task StopAsync_CancelsRunningTasks()
    {
        int totalTasks = 5;
        int unprocessedTaskCount = totalTasks;
        int completedTaskCount = 0;
        int cancelledTaskCount = 0;
        

        for (int i = 0; i < totalTasks; i++)
        {
            int taskNo = i;
            await this.queue.Enqueue(async (ct) =>
            {
                Interlocked.Decrement(ref unprocessedTaskCount);
                try
                {
                    await Task.Delay(200, ct);
                    Interlocked.Increment(ref completedTaskCount);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelledTaskCount);
                }
            });
        }

        await Task.Delay(300);
        await this.queue.StopAsync();

        // The first two tasks should have completed.
        // The second two tasks will start processing but will be cancelled.
        // The last task will not start processing.

        Assert.Equal(2, completedTaskCount);
        Assert.Equal(2, cancelledTaskCount);
        Assert.Equal(1, unprocessedTaskCount);
    }
}
