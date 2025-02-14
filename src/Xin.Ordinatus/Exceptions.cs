namespace Xin.Ordinatus.Exceptions;

public class QueueStoppedException : Exception
{
    public QueueStoppedException()
        : base("Queue has stopped.")
    {
    }
}
