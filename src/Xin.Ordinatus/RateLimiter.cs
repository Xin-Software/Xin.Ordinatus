using System.Diagnostics;
using System.Threading.Channels;

namespace Xin.Ordinatus;

public class RateLimiter : IAsyncDisposable
{
    private readonly int maxCalls;
    private readonly TimeSpan period;
    private readonly Channel<Func<CancellationToken, Task>> channel;
    private readonly CancellationTokenSource cts;
    private readonly Task processLoopTask;

    public RateLimiter(int maxCalls, TimeSpan period)
    {
        this.maxCalls = maxCalls;
        this.period = period;
        this.channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>();
        this.cts = new CancellationTokenSource();
        this.processLoopTask = Task.Run(this.ProcessLoopAsync);
    }

    public event Action<Exception>? OnError;

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

    public Task Enqueue(Func<CancellationToken, Task> task, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, Task> wrappedTask = async ct =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);

            try
            {
                await task(linkedCts.Token);
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

    public Task Enqueue<TInput>(TInput input, Func<TInput, Task> task, CancellationToken cancellationToken = default)
    {
        return this.Enqueue(() => task(input), cancellationToken);
    }

    private async Task ProcessLoopAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        int callCount = 0;

        while (await this.channel.Reader.WaitToReadAsync(this.cts.Token))
        {
            while (this.channel.Reader.TryRead(out var task))
            {
                if (callCount >= this.maxCalls)
                {
                    var elapsed = stopwatch.Elapsed;
                    var remaining = this.period - elapsed;

                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining, this.cts.Token);
                    }

                    callCount = 0;
                    stopwatch.Restart();
                }

                callCount++;
                try
                {
                    await task(this.cts.Token);
                }
                catch (Exception ex)
                {
                    this.OnError?.Invoke(ex);
                }
            }
        }
    }
}
