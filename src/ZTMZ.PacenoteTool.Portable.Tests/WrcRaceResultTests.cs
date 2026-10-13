using System.Runtime.InteropServices;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class WrcRaceResultTests
{
    [Theory]
    [InlineData(WRCStageResultStatus.FINISHED, GameStateRaceEnd.Normal, Constants.SYSTEM_END_STAGE)]
    [InlineData(WRCStageResultStatus.TIME_OUT_STAGE, GameStateRaceEnd.TimeOut, Constants.SYSTEM_RETIRED)]
    [InlineData(WRCStageResultStatus.TERMINALLY_DAMAGED, GameStateRaceEnd.Crashed, Constants.SYSTEM_RETIRED)]
    [InlineData(WRCStageResultStatus.RETIRED, GameStateRaceEnd.Retired, Constants.SYSTEM_RETIRED)]
    [InlineData(WRCStageResultStatus.DISQUALIFIED, GameStateRaceEnd.Disqualified, Constants.SYSTEM_RETIRED)]
    public void ExplicitResultWinsOverResetOrCompletedTelemetry(
        WRCStageResultStatus result, GameStateRaceEnd expected, string sound)
    {
        foreach (float progress in new[] { 0f, 0.5f, 1f })
        {
            var reader = new WRCGameDataReader { GameState = GameState.Racing };
            var events = new List<GameStateChangeEvent>();
            reader.onGameStateChanged += events.Add;
            var packet = Packet(result, progress);
            reader.onNewUdpMessage(new byte[packet.Length], packet);
            reader.onNewUdpMessage(packet, packet);
            var end = Assert.Single(events);
            Assert.Equal(GameState.RaceEnd, end.NewGameState);
            Assert.Equal(expected, Assert.IsType<GameStateRaceEnd>(end.Parameters[GameStateRaceEndProperty.FINISH_STATE]));
            Assert.Equal(sound, RaceEndPlaybackPolicy.GetSoundToken(end));
            Assert.Equal(expected == GameStateRaceEnd.Normal,
                RaceEndPlaybackPolicy.TryGetReplayFinishTime(end, out _));
        }
    }

    [Theory]
    [InlineData(WRCStageResultStatus.NOT_FINISHED)]
    [InlineData(WRCStageResultStatus.UNKNOWN)]
    [InlineData((WRCStageResultStatus)99)]
    public void DistanceAloneDoesNotInventAResult(WRCStageResultStatus result)
    {
        var reader = new WRCGameDataReader { GameState = GameState.Racing };
        var events = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += events.Add;
        var packet = Packet(result, 1);
        reader.onNewUdpMessage(new byte[packet.Length], packet);
        Assert.DoesNotContain(events, e => e.NewGameState == GameState.RaceEnd);
    }

    private static byte[] Packet(WRCStageResultStatus result, float progress)
    {
        var bytes = new byte[Marshal.SizeOf<WRCDataStructure>()];
        void Float(string field, float value) => BitConverter.GetBytes(value)
            .CopyTo(bytes, Marshal.OffsetOf<WRCDataStructure>(field).ToInt32());
        bytes[Marshal.OffsetOf<WRCDataStructure>(nameof(WRCDataStructure.stage_result_status)).ToInt32()] = (byte)result;
        Float(nameof(WRCDataStructure.stage_progress), progress);
        Float(nameof(WRCDataStructure.stage_current_time), progress == 0 ? 0 : 123);
        Float(nameof(WRCDataStructure.stage_result_time), 123.45f);
        return bytes;
    }
}
