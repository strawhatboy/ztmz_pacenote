using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc9Tests
{
    private static readonly WrcGenerationsNativeResult Running = new(1, 2, true, false, false, false, false);
    private static readonly WrcGenerationsNativeResult Retired = new(1, 2, true, true, true, true, true);
    private static Wrc9Snapshot Sample(int time, WrcGenerationsNativeResult? native = null) =>
        new("owner", "stage", "car", "WRC", time) { NativeResult = native ?? Running };

    private static Wrc9Snapshot? RecordedSnapshot(JsonElement s) => s.ValueKind == JsonValueKind.Null ? null :
        Sample(s.GetProperty("RawMilliseconds").GetInt32(), s.GetProperty("NativeResult").Deserialize<WrcGenerationsNativeResult>()) with
        {
            Identity = s.GetProperty("Identity").GetString()!,
            ResultMilliseconds = s.GetProperty("ResultMilliseconds").Deserialize<int?>(),
            PenaltyMilliseconds = s.GetProperty("PenaltyMilliseconds").GetInt32()
        };

    [Fact]
    public void CapturedRestartPauseAndRetirementOnlyAnnounceRetirement()
    {
        var session = new Wrc9RaceSession();
        var previous = GameState.Unknown;
        var endings = new List<long>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "wrc9-restart-retire-20261015.jsonl")))
        {
            using var doc = JsonDocument.Parse(line);
            var row = doc.RootElement;
            var s = row.GetProperty("snapshot");
            var sample = RecordedSnapshot(s);
            var now = row.GetProperty("elapsedMs").GetInt64();
            var next = session.Update(sample, now);
            Assert.Equal(row.GetProperty("expectedState").GetString(), next.ToString());
            if (next == GameState.RaceEnd && previous != next)
            {
                endings.Add(now);
                Assert.False(session.IsNewSession);
                Assert.Equal(GameStateRaceEnd.Retired, session.FinishState);
                var evt = new GameStateChangeEvent { LastGameState = previous, NewGameState = next, Parameters = session.EndParameters(sample!) };
                Assert.Equal(Constants.SYSTEM_RETIRED, RaceEndPlaybackPolicy.GetSoundToken(evt));
                Assert.False(RaceEndPlaybackPolicy.TryGetReplayFinishTime(evt, out _));
            }
            previous = next;
        }
        Assert.Equal(new long[] { 54780 }, endings);
    }

    [Fact]
    public void CapturedTorsbyFinishIncludesPenaltyOnceAndEmitsOneNormalEnding()
    {
        var session = new Wrc9RaceSession();
        var previous = GameState.Unknown;
        var endings = 0;
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "wrc9-finish-penalty-20261015.jsonl")))
        {
            using var doc = JsonDocument.Parse(line);
            var row = doc.RootElement;
            var sample = RecordedSnapshot(row.GetProperty("snapshot"));
            var next = session.Update(sample, row.GetProperty("elapsedMs").GetInt64());
            Assert.Equal(row.GetProperty("expectedState").GetString(), next.ToString());
            Assert.NotEqual(GameState.Unknown, next); // Includes the previously missing GO transition.
            if (next == GameState.RaceEnd && previous != next)
            {
                endings++;
                Assert.Equal(GameStateRaceEnd.Normal, session.FinishState);
                Assert.Equal(257588, sample!.RawMilliseconds);
                Assert.Equal(5000, sample.PenaltyMilliseconds);
                Assert.Equal(262588, sample.ResultMilliseconds);
                var parameters = session.EndParameters(sample);
                Assert.Equal(262.588f, parameters[GameStateRaceEndProperty.FINISH_TIME]);
                Assert.Equal(5f, parameters[GameStateRaceEndProperty.FINISH_TIME_PANALTY]);
            }
            previous = next;
        }
        Assert.Equal(1, endings);
    }

    [Fact]
    public void CountdownAndRestartCannotAnnounceAbandonmentBeforeGo()
    {
        var session = new Wrc9RaceSession();
        session.Update(Sample(12000), 0);
        Assert.Equal(GameState.CountingDown, session.Update(Sample(0, Running with { TimerStarted = false }), 50));
        Assert.True(session.IsNewSession);
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Retired), 100));
        Assert.Null(session.FinishState);
        session.Update(Sample(0, Running with { TimerStarted = false }), 150);
        session.Update(Sample(1000), 200);
        Assert.Equal(GameState.RaceEnd, session.Update(Sample(0, Retired), 250));
    }

    [Theory]
    [InlineData(100, GameState.RaceEnd)]
    [InlineData(2051, GameState.Unknown)]
    public void PartialResultHasBoundedGracePeriod(long completedAt, GameState expected)
    {
        var session = new Wrc9RaceSession();
        session.Update(Sample(1000), 0);
        Assert.Equal(GameState.Paused, session.Update(Sample(0, Retired with { ProfileReady = false, ProfileFailed = false }), 50));
        Assert.Equal(expected, session.Update(Sample(0, Retired), completedAt));
    }

    [Fact]
    public void MissingSessionOrLateAttachmentCannotAnnounceRetirement()
    {
        var session = new Wrc9RaceSession();
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Retired), 0));
        session.Update(Sample(1000), 50);
        Assert.Equal(GameState.Paused, session.Update(Sample(1000), 60000));
        Assert.Null(session.FinishState);
        Assert.Equal(GameState.Unknown, session.Update(null, 60050));
        Assert.Equal(GameState.Unknown, session.Update(Sample(0, Retired), 60100));
    }

    [Fact]
    public void VerifiedNormalResultCarriesTotalAndPenalty()
    {
        var session = new Wrc9RaceSession();
        session.Update(Sample(1000), 0);
        var result = Sample(100000, Running with { TimerEnded = true, ProfileReady = true }) with
            { ResultMilliseconds = 118000, PenaltyMilliseconds = 18000 };
        Assert.Equal(GameState.RaceEnd, session.Update(result, 100));
        var parameters = session.EndParameters(result);
        Assert.Equal(GameStateRaceEnd.Normal, parameters[GameStateRaceEndProperty.FINISH_STATE]);
        Assert.Equal(118f, parameters[GameStateRaceEndProperty.FINISH_TIME]);
        Assert.Equal(18f, parameters[GameStateRaceEndProperty.FINISH_TIME_PANALTY]);
    }

    [Fact]
    public void ProviderUsesOwnIdentityAndOptInBridge()
    {
        var game = new Wrc9();
        Assert.Equal("WRC9", game.Executable);
        Assert.Equal(1267540u, game.SteamApplicationId);
        Assert.IsAssignableFrom<IGameLivePacenoteSource>(game.GameDataReader);
        Assert.IsAssignableFrom<ILocalGameBridgeControl>(game);
        Assert.True(game.UseLivePacenotesOnly);
        Assert.True(game.AllowLivePacenotesBeforeGo);
        Assert.False(game.UseTelemetryGoSound);
        Assert.False(((Wrc9Config)game.GameConfigurations[Wrc9Config.Name]).ControlsEnabled);
    }

    private static WrcGenerationsPacenoteCatalog Catalog() => WrcGenerationsPacenoteCatalog.Parse("""
        PaceNotes = {
          ACUTE_HAIRPIN_RIGHT = { DetailedSound = "Atom_ACUTE_HAIRPIN_RIGHT" },
          m30 = { DetailedSound = "Atom_m30" },
          CHICANE_LEFT = { DetailedSound = "Atom_CHICANE_LEFT" },
        }
        """, """
        PaceNoteBlocks = {
          Block_0321 = { "ACUTE_HAIRPIN_RIGHT", "m30" },
          Block_0001 = { "CHICANE_LEFT" },
        }
        """);

    [Fact]
    public void ObservedUppercaseResourceEmitsWholeBlockWithVocabularyValidation()
    {
        const string resource = "DATA:/SOUNDS/VOICE/EN/PACENOTES/MALE/BLOCK_0321.PAF";
        var decoder = new Wrc9LivePacenoteDecoder(Catalog(), new HashSet<string> { "right_acute", "30", "right_entry_chicane" });
        Assert.True(decoder.TryDecode(123, 456, resource, out var evt));
        Assert.Equal("right_acute/30", evt!.Record.RawText);
        Assert.Equal(evt.Id, evt.DisplayGroupId);
        Assert.False(decoder.TryDecode(123, 456, resource.Replace("VOICE", "ENGINE"), out _));
        Assert.False(decoder.TryDecode(123, 456, resource.Replace("0321", "0001"), out _));
        Assert.False(decoder.TryDecode(123, 456, resource.Replace(".PAF", ".wav"), out _));
        var missing = new Wrc9LivePacenoteDecoder(Catalog(), new HashSet<string> { "right_acute" });
        Assert.False(missing.TryDecode(123, 456, resource, out _));
    }

    [Fact]
    public void CaseInsensitiveResolutionRejectsAmbiguousAtomsWithoutChangingWrcgLookup()
    {
        var catalog = WrcGenerationsPacenoteCatalog.Parse("""
        PaceNotes = {
          LEFT_1 = { DetailedSound = "Atom_Test" },
          RIGHT_1 = { DetailedSound = "Atom_test" },
        }
        """, """
        PaceNoteBlocks = {
          Block_0001 = { "LEFT_1" },
        }
        """);
        Assert.False(catalog.TryResolveIgnoringCase("ATOM_TEST", out _));
        Assert.True(catalog.TryResolve("Atom_Test", out var atoms));
        Assert.Equal(new[] { "LEFT_1" }, atoms);
    }

    [Theory]
    [InlineData("READY\n", "READY")]
    [InlineData("READY\r\n", "READY")]
    [InlineData("", null)]
    public async Task PipeFramingAcceptsCompleteBoundedLines(string text, string? expected)
    {
        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(expected, await Wrc9LiveSession.ReadLineAsync(reader, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipeFramingRejectsTruncatedAndOversizedRecords(bool oversized)
    {
        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(oversized ? new string('x', 1025) + "\n" : "READY")));
        await Assert.ThrowsAsync<InvalidDataException>(() => Wrc9LiveSession.ReadLineAsync(reader, CancellationToken.None));
    }

    [Fact]
    public void ProtectedIntegerFollowsTableMovesAndRejectsUnstableReads()
    {
        const ulong address = 0x123450;
        var slotReads = 0;
        uint slot = 7;
        byte[] Read(ulong p, int size)
        {
            Assert.Equal(4, size);
            var result = new byte[4];
            if (p == address + 8)
            {
                // Relocate on the first verification read; the bounded retry must follow it.
                if (++slotReads >= 2) slot = 8;
                BinaryPrimitives.WriteUInt32LittleEndian(result, slot);
            }
            else
            {
                Assert.Equal(0x10000UL + slot * 4, p);
                var mask = unchecked(((uint)(address + 8) + slot) * 0x159a55e5u + 0x159ae628u);
                BinaryPrimitives.WriteUInt32LittleEndian(result, 47607u ^ mask);
            }
            return result;
        }
        Assert.Equal(47607, Wrc9SessionMemory.ReadProtectedInteger(Read, () => 0x10000, address));
        var tableReads = 0;
        Assert.Null(Wrc9SessionMemory.ReadProtectedInteger(Read, () => ++tableReads % 2 == 0 ? 0x20000UL : 0x10000UL, address));
        Assert.Equal(6, tableReads);
    }
}
