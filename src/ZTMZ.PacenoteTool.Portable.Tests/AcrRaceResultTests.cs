using System.Text.Json;
using ZTMZ.PacenoteTool.AC;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class AcrRaceResultTests
{
    [Theory]
    [InlineData("StopAtMarshals", GameStateRaceEnd.Normal, Constants.SYSTEM_END_STAGE)]
    [InlineData("EventDisqualified", GameStateRaceEnd.Disqualified, Constants.SYSTEM_RETIRED)]
    [InlineData("EventDisqualified1", GameStateRaceEnd.Disqualified, Constants.SYSTEM_RETIRED)]
    [InlineData("EventDisqualified2", GameStateRaceEnd.Disqualified, Constants.SYSTEM_RETIRED)]
    public async Task BridgeEndingCuesPreserveTheirSoundWithoutInventingResultTime(
        string token, GameStateRaceEnd result, string sound)
    {
        var completion = new TaskCompletionSource<GameStateChangeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bridge = new ACRLivePacenoteBridge(_ => { }, (state, parameters) =>
        {
            if (state == GameState.RaceEnd)
                completion.TrySetResult(new GameStateChangeEvent
                {
                    LastGameState = GameState.Racing, NewGameState = state, Parameters = parameters!
                });
        }, _ => { }, listenForNamedPipe: false);
        Assert.True(bridge.EnqueueMessage(JsonSerializer.Serialize(new { Version = 1, Token = token, Tokens = new[] { token } })));
        var end = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(result, RaceEndPlaybackPolicy.GetFinishState(end));
        Assert.Equal(sound, RaceEndPlaybackPolicy.GetSoundToken(end));
        Assert.False(RaceEndPlaybackPolicy.TryGetReplayFinishTime(end, out _));
    }
}
