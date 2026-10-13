using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRGameStateDetectorTest
{
    [Fact]
    public void ReplayFollowedByNormalCountdownIsNotMarkedAsReplay()
    {
        var detector = new RBRGameStateDetector();

        var replayStart = detector.Detect(Data(RBRGameState.Replay, countdown: 5), true, GameState.Unknown, 0, 0);
        Assert.Equal(GameState.RaceBegin, replayStart.State);
        Assert.True(detector.IsReplaySession);

        var replayRacing = detector.Detect(Data(RBRGameState.Replay), true, GameState.RaceBegin, 0.5f, 20);
        Assert.Equal(GameState.Racing, replayRacing.State);
        Assert.True(detector.IsReplaySession);

        var menu = detector.Detect(Data(RBRGameState.Menu), true, GameState.Racing, 0, 0);
        Assert.Equal(GameState.Unknown, menu.State);
        Assert.False(detector.IsReplaySession);

        var normalStart = detector.Detect(Data(RBRGameState.RaceBegin, countdown: 5), false, GameState.Unknown, 0, 0);
        Assert.Equal(GameState.RaceBegin, normalStart.State);
        Assert.False(detector.IsReplaySession);
    }

    [Fact]
    public void SessionBoundaryResetsStateAndAllowsAnotherAdHocStart()
    {
        var detector = new RBRGameStateDetector();

        var firstStart = detector.Detect(Data(RBRGameState.Racing), false, GameState.Unknown, 0.3f, 12);
        Assert.Equal(GameState.AdHocRaceBegin, firstStart.State);
        Assert.True(detector.IsRacing);

        var menu = detector.Detect(Data(RBRGameState.Menu), false, GameState.Racing, 0, 0);
        Assert.Equal(GameState.Unknown, menu.State);
        Assert.False(detector.IsRacing);
        Assert.False(detector.IsPostGame);

        var nextStart = detector.Detect(Data(RBRGameState.Racing), false, GameState.Unknown, 0.3f, 12);
        Assert.Equal(GameState.AdHocRaceBegin, nextStart.State);
    }

    [Fact]
    public void CountdownBegunMidSequenceDoesNotAnnounceNumbersAlreadyMissed()
    {
        var detector = new RBRGameStateDetector();

        var begin = detector.Detect(Data(RBRGameState.RaceBegin, countdown: 2.5f), false, GameState.Unknown, 0, 0);
        Assert.Equal(GameState.RaceBegin, begin.State);
        Assert.Null(begin.CountdownNumber);

        var beforeTwo = detector.Detect(Data(RBRGameState.RaceBegin, countdown: 2.1f), false, GameState.RaceBegin, 0, 0);
        Assert.Equal(GameState.RaceBegin, beforeTwo.State);
        Assert.Null(beforeTwo.CountdownNumber);

        var two = detector.Detect(Data(RBRGameState.RaceBegin, countdown: 1.9f), false, GameState.RaceBegin, 0, 0);
        Assert.Equal(GameState.CountingDown, two.State);
        Assert.Equal(2, two.CountdownNumber);

        var betweenNumbers = detector.Detect(Data(RBRGameState.RaceBegin, countdown: 1.8f), false, GameState.CountingDown, 0, 0);
        Assert.Equal(GameState.CountingDown, betweenNumbers.State);
        Assert.Null(betweenNumbers.CountdownNumber);

        var one = detector.Detect(Data(RBRGameState.RaceBegin, countdown: 0.9f), false, GameState.CountingDown, 0, 0);
        Assert.Equal(GameState.CountingDown, one.State);
        Assert.Equal(1, one.CountdownNumber);
    }

    [Fact]
    public void FalseStartCannotBeClassifiedAsNormalFinish()
    {
        var detector = new RBRGameStateDetector();

        var start = detector.Detect(
            Data(RBRGameState.Racing, falseStart: true),
            false,
            GameState.Unknown,
            completionRate: 1,
            lapTime: 42);

        Assert.Equal(GameState.AdHocRaceBegin, start.State);
        Assert.True(detector.SawFalseStart);
        Assert.False(detector.IsPostGame);
    }

    [Fact]
    public void CompletionRecordsNormalFinishBeforeRaceEnd()
    {
        var detector = new RBRGameStateDetector();

        detector.Detect(Data(RBRGameState.Racing), false, GameState.Unknown, 0.5f, 20);
        var racing = detector.Detect(Data(RBRGameState.Racing), false, GameState.AdHocRaceBegin, 1, 73.25f);
        var raceEnd = detector.Detect(Data(RBRGameState.RaceEnd), false, GameState.Racing, 0, 0);

        Assert.Equal(GameState.Racing, racing.State);
        Assert.True(detector.IsPostGame);
        Assert.Equal(73.25f, detector.FinishTime);
        Assert.Equal(GameState.RaceEnd, raceEnd.State);
    }

    [Fact]
    public void EarlyRaceEndFlagIsNotEvidenceOfNormalCompletion()
    {
        var detector = new RBRGameStateDetector();
        detector.Detect(Data(RBRGameState.Racing), false, GameState.Unknown, 0.4f, 35);
        detector.Detect(Data(RBRGameState.Racing, raceEnded: true), false, GameState.AdHocRaceBegin, 0.4f, 35);
        var end = detector.Detect(Data(RBRGameState.RaceEnd), false, GameState.Paused, 0, 0);
        Assert.Equal(GameState.RaceEnd, end.State);
        Assert.True(detector.SawRaceEnded);
        Assert.False(detector.IsPostGame);
        Assert.Equal(0, detector.FinishTime);
    }

    private static RBRMemData Data(
        RBRGameState state,
        float countdown = -1,
        bool raceEnded = false,
        bool falseStart = false)
    {
        return new RBRMemData
        {
            GameStateId = (byte)state,
            StageStartCountdown = countdown,
            RaceEnded = raceEnded,
            FalseStart = falseStart
        };
    }
}
