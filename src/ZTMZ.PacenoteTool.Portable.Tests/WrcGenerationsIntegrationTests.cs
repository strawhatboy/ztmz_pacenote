using System.Buffers.Binary;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public class WrcGenerationsIntegrationTests
{
    [Theory]
    [InlineData(0, 2, 2, 6, true)]
    [InlineData(2, 2, 6, 0, false)]
    [InlineData(0, 2, 2, 7, false)]
    public void FmodCompatibilityUsesAllFourWindowsVersionParts(int major, int minor, int build, int revision, bool expected)
        => Assert.Equal(expected, WrcGenerationsIntegrationProvider.IsVerifiedFmodVersion(major, minor, build, revision));

    [Fact]
    public void BridgeControlsUseSharedContractAndRequireOptIn()
    {
        var game = new WrcGenerations();
        var bridge = Assert.IsAssignableFrom<ILocalGameBridgeControl>(game);
        Assert.False(bridge.GetLocalBridgeDiagnostics().IsEnabled);
        ((WrcGenerationsConfig)game.GameConfigurations[WrcGenerationsConfig.Name]).ControlsEnabled = true;
        Assert.True(bridge.GetLocalBridgeDiagnostics().IsEnabled);
        Assert.Equal("game.wrcg.warning", bridge.InjectionWarningKey);
    }
    [Fact]
    public async Task PipeHandshakeRejectsStaleCallsAndPublishesMappedCall()
    {
        if (!OperatingSystem.IsWindows()) return;
        var notes="PaceNotes = {\n LEFT_3 = { DetailedSound = \"Atom_LEFT_3\" },\n}\n";
        var blocks="PaceNoteBlocks = {\n Block_0000 = { \"LEFT_3\" },\n}\n";
        var pid=Random.Shared.Next(100000, int.MaxValue);
        var received=new TaskCompletionSource<LivePacenoteEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session=new WrcGenerationsLiveSession(pid,"",evt=>received.TrySetResult(evt),
            ()=>WrcGenerationsPacenoteCatalog.Parse(notes,blocks),new HashSet<string>{"3_left"});
        await using var client=new System.IO.Pipes.NamedPipeClientStream(".",$"ZTMZ.WRCG.Live.v1.{pid}",System.IO.Pipes.PipeDirection.Out,System.IO.Pipes.PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        await using var writer=new StreamWriter(client){AutoFlush=true};
        await writer.WriteLineAsync("READY");
        await session.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await writer.WriteLineAsync($"{Environment.TickCount64-10000}\tMALE/Block_0000");
        await writer.WriteLineAsync($"{Environment.TickCount64}\tMALE/Atom_LEFT_3");
        var result=await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("MALE/Atom_LEFT_3",result.SourceResourceName);
        Assert.Equal("3_left",Assert.Single(result.Record.Pacenotes).Note);
    }

    [Fact]
    public void LateAttachmentPreparesBeforeRacingAndRestartPreparesAgain()
    {
        var reader = new WrcGenerationsGameDataReader();
        var states = new List<GameState>();
        reader.onGameStateChanged += e => states.Add(e.NewGameState);
        void Send(float time, float distance)
        {
            var packet = new byte[280];
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(4,4),time);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(8,4),distance);
            reader.onNewUdpMessage([],packet);
        }
        Send(40,300);Send(40.1f,301);Send(40.084f,301);Send(0,-1);Send(.1f,1);
        Assert.Equal(new[]{GameState.AdHocRaceBegin,GameState.Racing,GameState.RaceBegin,GameState.Racing},states);
    }

    [Fact]
    public void TokenMappingPreservesDirectionAndShortModifier()
    {
        var vocabulary = new HashSet<string>{"3_left","short","hard","brake","tightens_to_2","twisty_left","2"};
        Assert.True(WrcGenerationsTokenMapper.TryMap("LEFT_3_SHORT",vocabulary,out var tokens));
        Assert.Equal(new[]{"3_left","short"},tokens);
        Assert.True(WrcGenerationsTokenMapper.TryMap("hard_brake",vocabulary,out tokens));
        Assert.Equal(new[]{"hard","brake"},tokens);
        Assert.True(WrcGenerationsTokenMapper.TryMap("LEFT_RIGHT_2",vocabulary,out tokens));
        Assert.Equal(new[]{"twisty_left","2"},tokens);
        Assert.False(WrcGenerationsTokenMapper.TryMap("unknown_warning",vocabulary,out tokens));
        Assert.Empty(tokens);
    }

    [Fact]
    public void ForwardedModeDoesNotRequireOrRewriteGameSettings()
    {
        var game = new WrcGenerations();
        ((WrcGenerationsConfig)game.GameConfigurations[WrcGenerationsConfig.Name]).ReceiveForwardedTelemetry=true;
        var checker=new WrcGenerationsPrerequisiteChecker(new MissingDocuments());
        Assert.True(checker.CheckPrerequisites(game).IsOK);
        checker.ForceFix(game);
    }
    private sealed class MissingDocuments : IGameDocumentsLocator
    {
        public IReadOnlyList<string> FindDocumentsDirectories(uint steamAppId)=>[];
    }
}
