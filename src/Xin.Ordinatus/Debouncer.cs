using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xin.Ordinatus;

public class Debouncer
{
    private readonly TimeSpan delay;
    private readonly object @lock = new ();

    private CancellationTokenSource cts;

    public Debouncer(int millisecondDelay)
    {
        this.delay = TimeSpan.FromMilliseconds(millisecondDelay);
    }

    public event Action<Exception> OnError;

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
}
