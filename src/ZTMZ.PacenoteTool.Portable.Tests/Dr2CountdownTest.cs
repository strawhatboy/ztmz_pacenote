using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class Dr2CountdownTest
{
    [Fact]
    public void UsesCapturedSimulationClockAndDoesNotAdvanceWhilePaused()
    {
        var countdown = new Dr2Countdown();
        countdown.Begin(134.41861f, true);
        Assert.Null(countdown.Update(134.41861f));
        Assert.Equal(5, countdown.Update(134.45194f));
        Assert.Equal(4, countdown.Update(135.45f));
        Assert.Equal(3, countdown.Update(136.45f));
        Assert.Null(countdown.Update(136.45f));
        Assert.Equal(2, countdown.Update(137.45f));
        Assert.Equal(1, countdown.Update(138.45f));
        Assert.Null(countdown.Update(139.47037f));
    }

    [Fact]
    public void DoesNotInventAFullCountdownWhenJoiningAnExistingStream()
    {
        var countdown = new Dr2Countdown();
        countdown.Begin(137f, false);
        Assert.Null(countdown.Update(137f));
        Assert.Null(countdown.Update(138f));
    }

    [Fact]
    public void DoesNotQueueMissedNumbersOrCarryCountdownAcrossRestart()
    {
        var countdown = new Dr2Countdown();
        countdown.Begin(10f, true);
        Assert.Null(countdown.Update(10f));
        Assert.Equal(5, countdown.Update(10.033f));
        Assert.Equal(2, countdown.Update(13.1f));
        countdown.Reset();
        Assert.Null(countdown.Update(14f));
        countdown.Begin(20f, true);
        Assert.Null(countdown.Update(20f));
        Assert.Equal(5, countdown.Update(20.033f));
    }

    [Fact]
    public void ResetPacketAndHandbrakeWaitDoNotStartCountdown()
    {
        var countdown = new Dr2Countdown();
        countdown.Begin(62.704605f, true);
        Assert.Null(countdown.Update(62.704605f));
        Assert.Null(countdown.Update(62.704605f));
        // Captured restart: no UDP for 30 seconds, then real start stream.
        Assert.Null(countdown.Update(84.62696f));
        Assert.Equal(5, countdown.Update(84.66029f));
        Assert.Equal(4, countdown.Update(85.66f));
        Assert.Equal(3, countdown.Update(86.66f));
        Assert.Equal(2, countdown.Update(87.66f));
        Assert.Equal(1, countdown.Update(88.66f));
        Assert.Null(countdown.Update(89.67852f));
    }
}
