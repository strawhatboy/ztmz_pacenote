using System.Buffers.Binary;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public class WrcGenerationsIntegrationTests
{
    [Theory]
    [InlineData("keep_into", "hold/into")]
    [InlineData("keep_in", "keep_in")]
    [InlineData("def_dont_cut", "important/dont_cut")]
    [InlineData("dont_cut", "dont_cut")]
    [InlineData("DONUT", "reverse_donut")]
    [InlineData("UNDER_BRIDGE", "under_bridge")]
    [InlineData("under_arch", "under_arch")]
    [InlineData("go_back", "go_back")]
    [InlineData("around_panels", "around_sign")]
    [InlineData("forest", "forest")]
    [InlineData("canyon", "canyon")]
    [InlineData("tightens_7", "tightens_to_7")]
    [InlineData("go_left", "left")]
    public void AuditedSemanticsResolveAgainstPackagedDatabase(string atom, string expected)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(AppContext.BaseDirectory, "TestData", "official-data.zdb"),
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT filename FROM pacenote_filenames";
        using var rows = command.ExecuteReader();
        var vocabulary = new HashSet<string>(StringComparer.Ordinal);
        while (rows.Read()) vocabulary.Add(rows.GetString(0));
        Assert.True(WrcGenerationsTokenMapper.TryMap(atom, vocabulary, out var tokens));
        Assert.Equal(expected.Split('/'), tokens);
    }

    [Fact]
    public void WrcGoComesFromCountdownRatherThanCrossingStartLine()
    {
        IGamePacenoteSourcePolicy policy = new WrcGenerations();
        Assert.False(policy.UseTelemetryGoSound);
        Assert.True(WrcGenerationsLiveSession.TryGetCountdownSound("ztmz:/countdown/0", out var go));
        Assert.Equal(ZTMZ.PacenoteTool.Base.Constants.SYSTEM_GO, go);
        Assert.False(WrcGenerationsLiveSession.TryGetCountdownSound("MALE/IMMERSION_START_02", out _));
        Assert.False(WrcGenerationsLiveSession.TryGetCountdownSound("event:/GUI/Start", out _));
        Assert.False(WrcGenerationsLiveSession.TryGetCountdownSound("ztmz:/countdown/6", out _));
    }

    [Fact]
    public async Task CountdownPipeUsesImmediateSystemSoundsAndRejectsStaleGo()
    {
        if (!OperatingSystem.IsWindows()) return;
        var pid = Random.Shared.Next(100000, int.MaxValue);
        var cues = new System.Collections.Concurrent.ConcurrentQueue<LivePacenoteEvent>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new WrcGenerationsLiveSession(pid, "", evt =>
        {
            cues.Enqueue(evt);
            if (evt.SystemSoundToken == ZTMZ.PacenoteTool.Base.Constants.SYSTEM_GO) completed.TrySetResult();
        }, () => WrcGenerationsPacenoteCatalog.Parse(
            "PaceNotes = {\n LEFT_3 = { DetailedSound = \"Atom_LEFT_3\" },\n}\n",
            "PaceNoteBlocks = {\n Block_0000 = { \"LEFT_3\" },\n}\n"), new HashSet<string>());
        await using var client = new System.IO.Pipes.NamedPipeClientStream(".", $"ZTMZ.WRCG.Live.v1.{pid}", System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        await using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync("READY");
        await session.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await writer.WriteLineAsync($"{Environment.TickCount64 - 10000}\tztmz:/countdown/0");
        for (var i = 3; i >= 0; --i)
            await writer.WriteLineAsync($"{Environment.TickCount64}\tztmz:/countdown/{i}");
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, cues.Count);
        Assert.Equal(new[] { "system_countdown_3", "system_countdown_2", "system_countdown_1", ZTMZ.PacenoteTool.Base.Constants.SYSTEM_GO }, cues.Select(c => c.SystemSoundToken));
        Assert.All(cues, cue => Assert.False(cue.SystemSoundSequential));
    }

    [Fact]
    public void FinishRequiresResultTimeAndDoesNotConfusePauseOrRestart()
    {
        long now = 0;
        var reader = new WrcGenerationsGameDataReader(() => now);
        var states = new List<GameStateChangeEvent>();
        reader.onGameStateChanged += states.Add;
        void Send(float lap, float result, float distance)
        {
            var packet = new byte[280];
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(4, 4), lap);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(8, 4), distance);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(248, 4), result);
            reader.onNewUdpMessage([], packet);
        }
        Send(20, -27833, 100);
        Send(21, -27833, 110);
        now = 1000;
        Send(21, -27833, 110);
        Assert.Equal(GameState.Paused, reader.GameState);
        Send(379.473f, -27833, 2264);
        Send(379.457f, 379457, 2264.675f);
        Send(379.457f, 379457, 2264.675f);
        var finish = Assert.Single(states.Where(e => e.NewGameState == GameState.RaceEnd));
        Assert.Equal(GameStateRaceEnd.Normal, finish.Parameters[GameStateRaceEndProperty.FINISH_STATE]);
        Assert.Equal(379.457f, (float)finish.Parameters[GameStateRaceEndProperty.FINISH_TIME]);
        Send(0, 0, -1);
        Assert.Equal(GameState.RaceBegin, reader.GameState);
        Send(.054f, -27833, -1);
        Assert.Equal(GameState.Racing, reader.GameState);
    }

    [Theory]
    [InlineData(20, 19000)]
    [InlineData(0, 0)]
    [InlineData(20, -27833)]
    [InlineData(20, 20000.5f)]
    public void InvalidOrUnrelatedResultIsNotFinish(float lap, float result)
    {
        var packet = new byte[280];
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(4, 4), lap);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(248, 4), result);
        Assert.False(WrcGenerationsTelemetry.TryGetFinishTime(packet, out _));
    }
    [Theory]
    [InlineData(GameState.RaceBegin, true)]
    [InlineData(GameState.CountingDown, true)]
    [InlineData(GameState.Racing, true)]
    [InlineData(GameState.Unknown, false)]
    [InlineData(GameState.Paused, false)]
    [InlineData(GameState.RaceEnd, false)]
    public void PreStartCallsAreAllowedOnlyInPreparedStageStates(GameState state, bool expected)
    {
        Assert.Equal(expected, GamePacenoteSourcePolicy.AllowsLivePlayback(new WrcGenerations(), state));
        var acr = (ZTMZ.PacenoteTool.AC.ACRally)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ZTMZ.PacenoteTool.AC.ACRally));
        Assert.Equal(state == GameState.Racing, GamePacenoteSourcePolicy.AllowsLivePlayback(acr, state));
    }
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
        await writer.WriteLineAsync($"{Environment.TickCount64}\tFEMALE/IMMERSION_START_02");
        await writer.WriteLineAsync($"{Environment.TickCount64}\tFEMALE/Atom_LEFT_3");
        var result=await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("FEMALE/Atom_LEFT_3",result.SourceResourceName);
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

    [Theory]
    [InlineData("CHICANE_LEFT", "right_entry_chicane")]
    [InlineData("CHICANE_RIGHT", "left_entry_chicane")]
    [InlineData("CHICANE_LEFTENTRY", "left_entry_chicane")]
    [InlineData("CHICANE_RIGHTENTRY", "right_entry_chicane")]
    public void ChicanesDistinguishFirstTurnFromEntrySide(string atom, string expected)
    {
        var catalog = WrcGenerationsPacenoteCatalog.Parse(
            $"PaceNotes = {{\n {atom} = {{ DetailedSound = \"Atom_{atom}\" }},\n}}\n",
            $"PaceNoteBlocks = {{\n Block_0000 = {{ \"{atom}\" }},\n}}\n");
        var vocabulary = new HashSet<string> { "left_entry_chicane", "right_entry_chicane" };
        foreach (var resource in new[] { $"MALE/Atom_{atom}", $"FEMALE/Atom_{atom}", "MALE/Block_0000" })
        {
            Assert.True(catalog.TryResolve(resource, out var atoms));
            Assert.True(WrcGenerationsTokenMapper.TryMap(Assert.Single(atoms), vocabulary, out var tokens));
            Assert.Equal(expected, Assert.Single(tokens));
        }
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
