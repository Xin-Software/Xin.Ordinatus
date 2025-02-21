using System.Diagnostics;

namespace Xin.Ordinatus;

/// <summary>
/// Provides a mechanism to debounce actions, ensuring that the action is only executed after a specified delay has elapsed since the last invocation.
/// </summary>
public class Debouncer
{
    private readonly TimeSpan delay;
    private readonly object @lock = new();

    private CancellationTokenSource? cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="Debouncer"/> class.
    /// </summary>
    /// <param name="delay">The delay to wait before executing the action.</param>
    public Debouncer(TimeSpan delay)
    {
        this.delay = delay;
    }

    /// <summary>
    /// Occurs when an error is encountered during the execution of the debounced action.
    /// </summary>
    public event Action<Exception>? OnError;

    /// <summary>
    /// Debounces the specified action, ensuring that it is only executed after the specified delay has elapsed since the last invocation.
    /// </summary>
    /// <param name="func">The action to debounce.</param>
    /// <remarks>
    /// If the method is called multiple times in quick succession, only the last invocation will be executed after the delay.
    /// </remarks>
    public void Debounce(Action func)
    {
        lock (@lock)
        {
            this.cts?.Cancel();
            this.cts?.Dispose();
            this.cts = new CancellationTokenSource();
        }

        _ = Task.Delay(delay, cts.Token)
            .ContinueWith(task =>
            {
                if (!task.IsCanceled)
                {
                    try
                    {
                        Debug.WriteLine("Executing");
                        func();
                    }
                    catch (Exception ex)
                    {
                        this.OnError?.Invoke(ex);
                    }
                }
                else
                {
                    Debug.WriteLine("Cancelled");
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// Clears the debounce, canceling any scheduled action.
    /// </summary>
    public void Clear()
    {
        lock (@lock)
        {
            this.cts?.Cancel();
            this.cts?.Dispose();
            this.cts = null;
        }
    }
}
