namespace Xin.Ordinatus.Tests;

public class DebouncerTests
{
    [Fact]
    public void Debounce_ExecutesAction()
    {
        bool actionExecuted = false;
        var debouncer = new Debouncer(10);
        debouncer.Debounce(() => actionExecuted = true);
        Thread.Sleep(20);
        Assert.True(actionExecuted, "The action was not executed.");
    }

    [Fact]
    public void Debounce_CancelsPreviousAction()
    {
        int executionCount = 0;
        var debouncer = new Debouncer(10);
        for (int i = 0; i < 10; i++)
        {
            debouncer.Debounce(() => Interlocked.Increment(ref executionCount));
        }
        Thread.Sleep(30);
        Assert.Equal(1, executionCount);
    }
}
