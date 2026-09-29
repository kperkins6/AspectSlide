using AspectSlide.Slideshow;

namespace AspectSlide.Tests;

public sealed class AppTimerTests
{
    [Fact]
    public async Task ArmedTimerInvokesCallbackOnce()
    {
        using var timer = new SystemAppTimer();
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;

        timer.Arm(TimeSpan.FromMilliseconds(20), () => { Interlocked.Increment(ref count); called.TrySetResult(); });
        await called.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        Assert.Equal(1, count);
    }
}
