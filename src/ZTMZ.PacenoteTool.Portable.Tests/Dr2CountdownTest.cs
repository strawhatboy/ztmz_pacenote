using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class Dr2CountdownTest
{
    private static Dr2StartLightsSnapshot Light(int phase, ulong address = 0x10000) => new(address, phase);

    [Fact]
    public void NativeWaitingStateNeverStartsCountdown()
    {
        var countdown = new Dr2Countdown();
        for (var i = 0; i < 1000; i++) Assert.Null(countdown.Update(Light(0)));
        Assert.Null(countdown.Update(null));
    }

    [Fact]
    public void CapturedNativeSequenceEmitsEachNumberOnce()
    {
        // 2026-10-17 online start: waiting at 21:19:03, phase 1 at
        // 21:19:30.820, then 2/3/4/5, phase 6 (GO) at 21:19:35.815.
        var countdown = new Dr2Countdown();
        Assert.Null(countdown.Update(Light(0)));
        for (var phase = 1; phase <= 5; phase++)
        {
            Assert.Equal(6 - phase, countdown.Update(Light(phase)));
            Assert.Null(countdown.Update(Light(phase))); // pause / unchanged sample
            Assert.Null(countdown.Update(null)); // failed read must not replay
            Assert.Null(countdown.Update(Light(phase)));
        }
        Assert.Null(countdown.Update(Light(6)));
        Assert.Null(countdown.Update(Light(6)));
    }

    [Fact]
    public void LateAttachmentAndMissedReadsDoNotInventPreviousNumbers()
    {
        var countdown = new Dr2Countdown();
        Assert.Equal(3, countdown.Update(Light(3)));
        Assert.Equal(1, countdown.Update(Light(5)));
        Assert.Null(countdown.Update(Light(4))); // stale / backwards snapshot
        Assert.Null(countdown.Update(Light(5)));
    }

    [Fact]
    public void NativeResetAndNewSessionAllowAnotherCountdown()
    {
        var countdown = new Dr2Countdown();
        Assert.Equal(5, countdown.Update(Light(1)));
        Assert.Null(countdown.Update(Light(6)));
        Assert.Null(countdown.Update(Light(0)));
        Assert.Equal(5, countdown.Update(Light(1)));
        Assert.Equal(5, countdown.Update(Light(1, 0x20000)));
        countdown.Reset();
        Assert.Equal(5, countdown.Update(Light(1, 0x20000)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(255)]
    public void InvalidPhaseIsNotAnnounced(int phase)
    {
        Assert.Null(new Dr2Countdown().Update(Light(phase)));
    }
}
