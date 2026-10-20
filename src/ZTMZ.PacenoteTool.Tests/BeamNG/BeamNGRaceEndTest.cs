using System.Text;
using Neo.IronLua;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.BeamNG;

namespace ZTMZ.PacenoteTool.Tests.BeamNG;

public class BeamNGRaceEndTest
{
    [Theory]
    [InlineData("retired", GameStateRaceEnd.Retired)]
    [InlineData("finished", GameStateRaceEnd.Normal)]
    [InlineData("restart", null)]
    [InlineData("interrupted", null)]
    [InlineData("future-result", null)]
    public void ResultPacketsEndOnceAndAllowAnotherAttempt(string result, GameStateRaceEnd? expected)
    {
        var reader = new BeamNGGameDataReader();
        var changes = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += changes.Add;
        void Send(string resultValue) {
            var json = $$$"""
                {"magic":"ZTMZ_BNG_RALLY","protocolVersion":1,"type":"stage",
                 "stage":{"id":"ss-test","isActive":true,"stageTimeSeconds":42.5,"result":"{{{resultValue}}}"}}
                """;
            Assert.True(BeamNGRallyBridgePacket.TryParse(Encoding.UTF8.GetBytes(json), out var packet));
            reader.ProcessBridgePacket(packet!);
        }
        Send("");
        Send("");
        Assert.Equal(GameState.Racing, reader.GameState);
        Send(result);
        Send(result);
        reader.ProcessBridgePacket(new BeamNGRallyBridgePacket { Type = "telemetry" });
        var end = Assert.Single(changes.Where(e => e.NewGameState == GameState.RaceEnd));
        if (expected.HasValue)
            Assert.Equal(expected.Value, Assert.IsType<GameStateRaceEnd>(end.Parameters[GameStateRaceEndProperty.FINISH_STATE]));
        else
            Assert.False(end.Parameters.ContainsKey(GameStateRaceEndProperty.FINISH_STATE));
        Assert.Equal(expected == GameStateRaceEnd.Normal, end.Parameters.ContainsKey(GameStateRaceEndProperty.FINISH_TIME));

        Send("");
        Assert.Equal(GameState.RaceBegin, reader.GameState);
        Send("");
        Assert.Equal(GameState.Racing, reader.GameState);
        Send(result);
        Assert.Equal(2, changes.Count(e => e.NewGameState == GameState.RaceEnd));
    }

    // Exercise the shipped Lua, not a C# reimplementation of its event logic.
    [Theory]
    [InlineData("bridge.onAnyMissionWillChange('stopped', {id='ss-test'}, true)", "retired")]
    [InlineData("bridge.onAnyMissionWillChange('stopped', {id='ss-test'}, false)", "interrupted")]
    [InlineData("stageData.isComplete=true; bridge.onAnyMissionWillChange('stopped', {id='ss-test'}, true)", "finished")]
    [InlineData("bridge.onAnyMissionWillChange('stopped', {id='unrelated'}, true)", "")]
    [InlineData("bridge.onAnyMissionWillChange('started', {id='ss-test'}, true)", "")]
    [InlineData("setLoop('serviceOut'); bridge.onAnyMissionWillChange('stopped', {id='loop-test'}, true)", "")]
    [InlineData("setLoop('ss-test'); bridge.onAnyMissionWillChange('stopped', {id='loop-test'}, true)", "retired")]
    [InlineData("setLoop('ss-test'); attempt('abandon', 'ss-test')", "retired")]
    [InlineData("setLoop('ss-test'); attempt('restart', 'ss-test')", "restart")]
    [InlineData("setLoop('serviceOut'); attempt('abandon', 'serviceOut')", "")]
    [InlineData("setLoop('ss-test'); attempt('abandon', 'other-stage')", "")]
    [InlineData("setLoop('ss-test'); attempt('unknown', 'ss-test')", "")]
    public void LuaBridgeUsesExplicitScopedMissionEvents(string scenario, string expected)
    {
        using var lua = new Lua();
        var environment = CreateBridge(lua);
        environment.DoChunk(scenario, "scenario.lua");
        var value = environment.DoChunk("return lastPacket.stage and lastPacket.stage.result or ''", "result.lua");
        Assert.Equal(expected, value[0]);
        if (expected.Length == 0) return;

        environment.DoChunk("""
            local count = sendCount
            bridge.onAnyMissionWillChange('stopped', {id='ss-test'}, true)
            assert(sendCount == count, 'duplicate stop should not send a new result')
            local result = lastPacket.stage.result
            bridge.onUpdate(0.11)
            assert(lastPacket.stage.result == result, 'periodic packet lost terminal result')
            assert(lastPacket.pacenote == nil, 'ended stage published a pacenote')
            stageData.isComplete = false
            bridge.onRallyDataUpdated({activeState='countdown', countdownData={countdown=5}})
            bridge.onUpdate(0.11)
            assert(lastPacket.stage.result == nil, 'restart retained old result')
            assert(lastPacket.stage.isCountingDown, 'restart lost countdown')
            """, "terminal-and-restart.lua");
    }

    [Fact]
    public void LuaBridgeManagerReloadAndMissionStartClearOldResult()
    {
        using var lua = new Lua();
        var environment = CreateBridge(lua);
        environment.DoChunk("""
            bridge.onAnyMissionWillChange('stopped', {id='ss-test'}, true)
            bridge.onAnyMissionChanged('started', {id='unrelated'})
            bridge.onUpdate(0.11)
            assert(lastPacket.stage.result == 'retired', 'unrelated mission reset rally')
            bridge.onAnyMissionChanged('started', {id='ss-test'})
            bridge.onUpdate(0.11)
            assert(lastPacket.stage.result == nil, 'new attempt retained result')
            bridge.onAnyMissionWillChange('stopped', {id='ss-test'}, true)
            manager = nil
            bridge.onUpdate(2)
            assert(lastPacket.type == 'status' and lastPacket.status == 'waiting')
            newManager()
            bridge.onUpdate(0.11)
            assert(lastPacket.stage.result == nil, 'new manager retained result')
            """, "reload.lua");
    }

    [Fact]
    public void LuaBridgeStaysLoadedAcrossMapsAndReloadsSameVehicleId()
    {
        using var lua = new Lua();
        var environment = CreateBridge(lua);
        environment.DoChunk("""
            assert(invalidUnloadRegistrations == 0, 'registered before BeamNG assigned the extension name')
            assert(unloadMode == 'manual', 'bridge will be unloaded on map change')
            queued = 0
            function getPlayerVehicle() return {
              getId=function() return 123 end,
              queueLuaCommand=function() queued=queued+1 end
            } end
            bridge.onUpdate(0.6)
            assert(queued == 2, 'vehicle reporters were not loaded')
            bridge.onVehicleTelemetry(123, {maxRPM=7000, steering=0.5})
            stageData.isComplete = true
            bridge.onAnyMissionWillChange('stopped', {id='ss-test'}, false)
            if bridge.onClientEndMission then bridge.onClientEndMission() end
            -- BeamNG unloads all extensions except those registered as manual.
            if unloadMode ~= 'manual' then bridge.onExtensionUnloaded(); bridge=nil end
            assert(bridge ~= nil, 'bridge disappeared between maps')
            stageData = {isActive=true, currentSSTime=0}
            newManager()
            manager.missionId = 'ss-next'
            manager.getLastPlayedPacenote = function() return {id='note-next', name='right 3'} end
            bridge.onUpdate(0.11)
            assert(queued == 4, 'same vehicle ID on new map retained old reporter registration')
            assert(lastPacket.stage.id == 'ss-next' and lastPacket.stage.result == nil)
            assert(lastPacket.pacenote.id == 'note-next', 'next track lost pacenotes')
            assert(not lastPacket.telemetry.hasDetailedTelemetry, 'next track retained old telemetry')
            bridge.onVehicleTelemetry(123, {maxRPM=8000, steering=-0.5})
            bridge.onUpdate(0.11)
            assert(lastPacket.telemetry.maxRPM == 8000 and lastPacket.telemetry.steering == -0.5)
            """, "map-transition.lua");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LuaBridgeMissionIdentityChangeClearsTerminalStateWithoutManagerReplacement(bool notifyMissionStart)
    {
        using var lua = new Lua();
        var environment = CreateBridge(lua);
        environment["notifyMissionStart"] = notifyMissionStart;
        environment.DoChunk("""
            bridge.onRallyDataUpdated({activeState='countdown', countdownData={countdown=5}})
            stageData.isComplete = true
            bridge.onAnyMissionWillChange('stopped', {id='ss-test'}, false)
            assert(lastPacket.stage.result == 'finished')
            manager.missionId = 'ss-next'
            stageData = {isActive=true, currentSSTime=0}
            manager.getLastPlayedPacenote = function() return {id='next-note', name='left 3'} end
            manager.notebook = {pacenotes={sorted={{id='next-note', name='left 3'}}}}
            if notifyMissionStart then bridge.onAnyMissionChanged('started', {id='ss-next'}) end
            bridge.onUpdate(0.11)
            assert(lastPacket.type == 'stagePacenotes' and lastPacket.stage.id == 'ss-next')
            assert(not lastPacket.stage.isCountingDown, 'new track retained old countdown')
            bridge.onUpdate(0.11)
            assert(lastPacket.type == 'stage' and lastPacket.stage.result == nil)
            assert(lastPacket.pacenote.id == 'next-note', 'previous terminal stage suppressed next pacenote')
            """, "mission-transition.lua");
    }

    private static LuaGlobal CreateBridge(Lua lua)
    {
        var environment = lua.CreateEnvironment();
        environment["_G"] = environment;
        environment.DoChunk("""
            sendCount = 0
            function require(name)
              assert(name == 'socket')
              return {udp=function() return {
                settimeout=function() end, setpeername=function() return true end,
                send=function(self, packet) lastPacket=packet; sendCount=sendCount+1; return 1 end,
                close=function() end
              } end}
            end
            function jsonEncode(packet) return packet end
            invalidUnloadRegistrations = 0
            function setExtensionUnloadMode(extension, mode)
              if type(extension.__extensionName__) ~= 'string' then
                invalidUnloadRegistrations = invalidUnloadRegistrations + 1
                return
              end
              unloadMode = mode
            end
            stageData = {isActive=true, currentSSTime=42.5}
            function newManager()
              manager = {missionId='ss-test', getActiveStageData=function() return stageData end}
            end
            newManager()
            gameplay_rally = {getRallyManager=function() return manager end}
            function setLoop(current)
              gameplay_rallyLoop = {getManager=function() return {
                getCurrentMissionId=function() return current end,
                getRallyLoopMissionId=function() return 'loop-test' end
              } end}
            end
            function attempt(kind, stage)
              bridge.onMissionAttemptAggregated({dnf=true, data={dnfType=kind, dnfMissionId=stage}}, {id='loop-test'})
            end
            """, "beamng-stubs.lua");
        var source = Path.Combine(AppContext.BaseDirectory, "BeamNGMod", "scripts", "ztmz_pacenote_rally_bridge", "extension.lua");
        environment["bridge"] = environment.DoChunk(File.ReadAllText(source), "extension.lua")[0];
        // BeamNG requires the module first, assigns its name, then invokes onExtensionLoaded.
        environment.DoChunk("bridge.__extensionName__='scripts_ztmz__pacenote__rally__bridge_extension'; bridge.onExtensionLoaded(); bridge.onUpdate(0.11)", "start.lua");
        return environment;
    }
}
