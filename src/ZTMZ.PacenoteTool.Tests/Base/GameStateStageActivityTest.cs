using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class GameStateStageActivityTest
{
    [Theory]
    [InlineData(GameState.RaceBegin)]
    [InlineData(GameState.AdHocRaceBegin)]
    [InlineData(GameState.CountingDown)]
    [InlineData(GameState.Racing)]
    [InlineData(GameState.Paused)]
    public void ActiveStageStatesKeepTheOverlayVisible(GameState state)
    {
        Assert.True(GameStateStageActivity.IsActive(state));
    }

    [Theory]
    [InlineData(GameState.Unknown)]
    [InlineData(GameState.RaceEnd)]
    public void InactiveStatesKeepTheOverlayHidden(GameState state)
    {
        Assert.False(GameStateStageActivity.IsActive(state));
    }
}
