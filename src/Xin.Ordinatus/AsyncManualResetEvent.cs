namespace Xin.Ordinatus;

/// <summary>
/// Async implementation of a manual reset event.
/// When the event is set, awaiting `WaitAsync` will return immediately. Otherwise, it will wait until the event is set.
/// </summary>
public class AsyncManualResetEvent : IAsyncDisposable
{
    private volatile TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool isSet;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncManualResetEvent"/> class.
    /// </summary>
    /// <param name="initialState">Initial event state. True to allow tasks to continue, False to await tasks. Default is False.</param>
    public AsyncManualResetEvent(bool initialState = false)
    {
        this.isSet = initialState;
        if (this.isSet)
        {
            this.tcs.TrySetResult(true); // Start in the signaled state if specified
        }
    }

    /// <summary>
    /// Wait asynchronously until the event is set or the cancellation token is triggered.
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (this.isSet)
        {
            return;
        }

        var currentTcs = this.tcs;
        var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);

        if (await Task.WhenAny(currentTcs.Task, cancellationTask) == cancellationTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Sets the event, allowing all waiting tasks to proceed.
    /// </summary>
    public void Set()
    {
        if (this.isSet)
        {
            return;
        }

        this.isSet = true;
        this.tcs.TrySetResult(true); // Signal all waiting tasks
    }

    /// <summary>
    /// Resets the event, causing tasks to wait again.
    /// </summary>
    public void Reset()
    {
        if (!this.isSet)
        {
            return;
        }

        this.isSet = false;

        // Replace the current TaskCompletionSource with a new one
        var newTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref this.tcs, newTcs);
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async ValueTask DisposeAsync()
    {
        // Nothing to dispose, kept for future compatibility
    }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
}
