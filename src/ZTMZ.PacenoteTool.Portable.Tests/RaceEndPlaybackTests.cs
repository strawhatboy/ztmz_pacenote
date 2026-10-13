using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class RaceEndPlaybackTests
{
    [Theory]
    [InlineData(GameStateRaceEnd.Normal, Constants.SYSTEM_END_STAGE)]
    [InlineData(GameStateRaceEnd.Retired, Constants.SYSTEM_RETIRED)]
    [InlineData(GameStateRaceEnd.Crashed, Constants.SYSTEM_RETIRED)]
    [InlineData(GameStateRaceEnd.TimeOut, Constants.SYSTEM_RETIRED)]
    [InlineData(GameStateRaceEnd.Disqualified, Constants.SYSTEM_RETIRED)]
    [InlineData(GameStateRaceEnd.Unknown, null)]
    [InlineData((GameStateRaceEnd)99, null)]
    public void AllGamesUseExplicitResultForEndVoice(GameStateRaceEnd result, string? sound)
    {
        Assert.Equal(sound, RaceEndPlaybackPolicy.GetSoundToken(End(result)));
    }

    [Fact]
    public void MissingOrForeignResultCannotPlayCongratulationsOrSaveReplay()
    {
        foreach (var result in new object?[] { null, 1, "Normal", WRCStageResultStatus.FINISHED })
        {
            var evt = new GameStateChangeEvent();
            if (result is not null)
                evt.Parameters[GameStateRaceEndProperty.FINISH_STATE] = result;
            Assert.Null(RaceEndPlaybackPolicy.GetSoundToken(evt));
            Assert.False(RaceEndPlaybackPolicy.TryGetReplayFinishTime(evt, out _));
        }
    }

    [Fact]
    public void AudioOnlyFinishCanPlayCongratulationsWithoutInventingReplayTime()
    {
        var evt = End(GameStateRaceEnd.Normal);
        Assert.Equal(Constants.SYSTEM_END_STAGE, RaceEndPlaybackPolicy.GetSoundToken(evt));
        Assert.False(RaceEndPlaybackPolicy.TryGetReplayFinishTime(evt, out _));
        evt.Parameters[GameStateRaceEndProperty.FINISH_TIME] = 123.45f;
        Assert.True(RaceEndPlaybackPolicy.TryGetReplayFinishTime(evt, out var time));
        Assert.Equal(123.45f, time);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidResultTimeIsNotSaved(float time)
    {
        var evt = End(GameStateRaceEnd.Normal);
        evt.Parameters[GameStateRaceEndProperty.FINISH_TIME] = time;
        Assert.False(RaceEndPlaybackPolicy.TryGetReplayFinishTime(evt, out _));
    }

    [Theory]
    [InlineData(GameStateRaceEnd.Retired)]
    [InlineData(GameStateRaceEnd.Crashed)]
    [InlineData(GameStateRaceEnd.TimeOut)]
    [InlineData(GameStateRaceEnd.Disqualified)]
    [InlineData(GameStateRaceEnd.Unknown)]
    public void NonFinishWithElapsedTimeIsNotSavedAsCompletedReplay(GameStateRaceEnd result)
    {
        var evt = End(result);
        evt.Parameters[GameStateRaceEndProperty.FINISH_TIME] = 123.45f;
        Assert.False(RaceEndPlaybackPolicy.TryGetReplayFinishTime(evt, out _));
    }

    private static GameStateChangeEvent End(GameStateRaceEnd result) => new()
    {
        LastGameState = GameState.Racing, NewGameState = GameState.RaceEnd,
        Parameters = new() { [GameStateRaceEndProperty.FINISH_STATE] = result }
    };
}
