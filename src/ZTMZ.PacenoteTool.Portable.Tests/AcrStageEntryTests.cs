using System.Runtime.CompilerServices;
using ZTMZ.PacenoteTool.AC;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class AcrStageEntryTests
{
    private static ACRStageState Stage(int phase, int id = 2) => new(0x20000, 0x60000, 0xa0000, id, phase);

    [Fact]
    public void ProductionCaptureWelcomesAtHudEntryButNotAfterRestart()
    {
        var entry = new ACRStageEntry();
        var welcomes = 0;
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory,
                     "TestData", "acr-stage-entry-restart-20261020.jsonl")))
        {
            using var json = System.Text.Json.JsonDocument.Parse(line);
            var row = json.RootElement;
            var cue = entry.Observe(Stage(row.GetProperty("phase").GetInt32(), row.GetProperty("raceId").GetInt32()));
            Assert.Equal(row.GetProperty("expectedWelcome").GetBoolean(), cue is not null);
            if (cue is not null) welcomes++;
        }
        Assert.Equal(1, welcomes);
    }

    [Fact]
    public void CapturedStartSequenceWelcomesBeforeSemaphoreOnlyOnce()
    {
        var entry = new ACRStageEntry();
        // Native phases captured 2026-10-14: waiting -> countdown -> driving -> exit.
        var cue = Assert.IsType<LivePacenoteEvent>(entry.Observe(Stage(4)));
        Assert.Equal(Constants.SYSTEM_START_STAGE, cue.SystemSoundToken);
        Assert.True(cue.SystemSoundSequential);
        Assert.Null(entry.Observe(Stage(4)));
        Assert.Null(entry.Observe(null));
        Assert.Null(entry.Observe(Stage(4))); // failed reads / pause do not repeat it
        Assert.Null(entry.Observe(Stage(5)));
        Assert.Null(entry.Observe(Stage(6)));
        Assert.Null(entry.Observe(Stage(0, -1))); // exit does not infer retirement
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void AttachingOutsideWaitingPhaseDoesNotPlayWelcome(int phase)
    {
        Assert.Null(new ACRStageEntry().Observe(Stage(phase)));
        Assert.Null(new ACRStageEntry().Observe(Stage(4, -1)));
    }

    [Fact]
    public void RestartsRemainSilentUntilLoadedSessionChanges()
    {
        var entry = new ACRStageEntry();
        Assert.NotNull(entry.Observe(Stage(4)));
        entry.Observe(Stage(5));
        entry.Observe(Stage(6));
        Assert.Null(entry.Observe(Stage(4)));
        Assert.Null(entry.Observe(Stage(4)));
        Assert.Null(entry.Observe(Stage(0, -1)));
        Assert.Null(entry.Observe(null));
        Assert.Null(entry.Observe(Stage(1, -1)));
        Assert.Null(entry.Observe(Stage(2, 3)));
        Assert.Null(entry.Observe(Stage(4, 3)));
        Assert.NotNull(entry.Observe(Stage(4, 3) with { World = 0xb0000 }));
        entry.Reset();
        Assert.NotNull(entry.Observe(Stage(4, 3)));
    }

    [Fact]
    public void LoadedStageHudWelcomesAtStartSequenceWithoutRepeatingAtPreSemaphore()
    {
        var entry = new ACRStageEntry();
        // 2026-10-20: production reader phase 3, user confirmed the stage HUD.
        Assert.Null(entry.Observe(Stage(2)));
        Assert.NotNull(entry.Observe(Stage(3)));
        Assert.Null(entry.Observe(Stage(3)));
        Assert.Null(entry.Observe(null));
        Assert.Null(entry.Observe(Stage(4)));
        Assert.Null(entry.Observe(Stage(5)));
        Assert.Null(entry.Observe(Stage(6)));
        Assert.Null(entry.Observe(Stage(3)));
        Assert.Null(entry.Observe(Stage(4)));
    }

    [Theory]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void LateAttachmentDoesNotWelcomeOnRestart(int phase)
    {
        var entry = new ACRStageEntry();
        Assert.Null(entry.Observe(Stage(phase)));
        Assert.Null(entry.Observe(null));
        Assert.Null(entry.Observe(Stage(0, -1)));
        Assert.Null(entry.Observe(Stage(3, 3)));
        Assert.Null(entry.Observe(Stage(4, 3)));
        Assert.NotNull(entry.Observe(Stage(3, 0) with { World = 0xb0000 }));
    }

    [Fact]
    public void NewControllerOrOwnedStateAllowsWelcomeForNewSession()
    {
        var entry = new ACRStageEntry();
        Assert.NotNull(entry.Observe(Stage(3)));
        Assert.NotNull(entry.Observe(Stage(3) with { Controller = 0xb0000 }));
        Assert.Null(entry.Observe(Stage(4) with { Controller = 0xb0000 }));
        Assert.NotNull(entry.Observe(Stage(3) with { Controller = 0xb0000, State = 0xc0000 }));
    }

    [Theory]
    [InlineData("Stage B", "forward")]
    [InlineData("Stage A", "reverse")]
    public void DifferentTrackOrLayoutWelcomesEvenWhenNativeObjectsAreReused(string name, string layout)
    {
        var entry = new ACRStageEntry();
        Assert.NotNull(entry.Observe(Stage(3, 0), "Stage A", "forward"));
        Assert.Null(entry.Observe(Stage(6, 0), "Stage A", "forward"));
        Assert.Null(entry.Observe(Stage(0, -1), "Stage A", "forward"));
        Assert.Null(entry.Observe(Stage(3, 1), "Stage A", "forward")); // restart
        Assert.NotNull(entry.Observe(Stage(3, 2), name, layout)); // new track
        Assert.Null(entry.Observe(Stage(4, 2), name, layout));
        Assert.Null(entry.Observe(Stage(6, 2), name, layout));
        Assert.Null(entry.Observe(Stage(3, 3), name, layout)); // restart new track
        Assert.NotNull(entry.Observe(Stage(3, 4), "Stage A", "forward")); // return to A
    }

    [Fact]
    public void MissingOrInitiallyUnavailableTrackMetadataDoesNotRepeatWelcome()
    {
        var entry = new ACRStageEntry();
        Assert.NotNull(entry.Observe(Stage(3, 0)));
        Assert.Null(entry.Observe(Stage(3, 0), "Stage A", "forward"));
        Assert.Null(entry.Observe(null, "Stage B", "reverse"));
        Assert.Null(entry.Observe(Stage(0, -1), "", ""));
        Assert.Null(entry.Observe(Stage(3, 1), "  Stage A  ", "forward"));
        Assert.Null(entry.Observe(Stage(3, 1), null, null));
        Assert.Null(entry.Observe(Stage(4, 1), "Stage A", "forward"));
    }

    [Fact]
    public void DelayedTrackUpdateAllowsNewTrackButDoesNotDuplicateNewSessionWelcome()
    {
        var entry = new ACRStageEntry();
        Assert.NotNull(entry.Observe(Stage(3, 0), "Stage A", ""));
        Assert.Null(entry.Observe(Stage(6, 0), "Stage A", ""));
        Assert.Null(entry.Observe(Stage(3, 1), "Stage A", "")); // metadata not updated yet
        Assert.NotNull(entry.Observe(Stage(3, 1), "Stage B", ""));
        Assert.Null(entry.Observe(Stage(4, 1), "Stage B", ""));
        var nextSession = Stage(3, 0) with { World = 0xb0000 };
        Assert.NotNull(entry.Observe(nextSession, "Stage B", ""));
        Assert.Null(entry.Observe(nextSession, "Stage C", "")); // arrives after welcome
    }

    [Fact]
    public void TrackChangeObservedDuringDrivingDoesNotDeferWelcomeToRestart()
    {
        var entry = new ACRStageEntry();
        Assert.NotNull(entry.Observe(Stage(3, 0), "Stage A", ""));
        Assert.Null(entry.Observe(Stage(6, 1), "Stage B", ""));
        Assert.Null(entry.Observe(Stage(3, 2), "Stage B", ""));
    }

    [Theory]
    [InlineData(true, GameState.Unknown, GameState.RaceBegin, true)]
    [InlineData(false, GameState.Unknown, GameState.RaceBegin, false)]
    [InlineData(true, GameState.Paused, GameState.RaceBegin, false)]
    [InlineData(true, GameState.Unknown, GameState.AdHocRaceBegin, false)]
    [InlineData(true, GameState.Unknown, GameState.Racing, false)]
    public void CountdownNeverStartsAcrWelcomeAndOtherGamesRetainTheirPolicy(
        bool enabled, GameState oldState, GameState state, bool otherGameExpected)
    {
        var evt = new GameStateChangeEvent { LastGameState = oldState, NewGameState = state };
        var acr = (ACRally)RuntimeHelpers.GetUninitializedObject(typeof(ACRally));
        var dirt = (DirtRally)RuntimeHelpers.GetUninitializedObject(typeof(DirtRally));
        Assert.False(RaceStartPlaybackPolicy.ShouldPlay(acr, evt, enabled));
        Assert.Equal(otherGameExpected, RaceStartPlaybackPolicy.ShouldPlay(dirt, evt, enabled));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void LiveWelcomeHonorsItsOwnSettingWithoutChangingCountdown(bool welcome, bool go)
    {
        Assert.Equal(welcome, RaceStartPlaybackPolicy.AllowsLiveSound(Constants.SYSTEM_START_STAGE, welcome, go));
        Assert.Equal(go, RaceStartPlaybackPolicy.AllowsLiveSound(Constants.SYSTEM_GO, welcome, go));
        Assert.True(RaceStartPlaybackPolicy.AllowsLiveSound(Constants.SYSTEM_COUNTDOWNS[4], welcome, go));
    }

    [Fact]
    public void NativeReaderUsesOwnedNonReplayStateWithoutRequiringVehicle()
    {
        var memory = new Memory();
        Assert.Equal(Stage(4), ACRStageMemory.Read(memory.Read, Memory.Image));
        memory.Put(0xa0120, 6);
        Assert.Equal(Stage(6), ACRStageMemory.Read(memory.Read, Memory.Image));
    }

    [Theory]
    [InlineData(0x900a0, 0xb0000)] // foreign manager owner
    [InlineData(0xa00a0, 0xb0000)] // foreign race state owner
    [InlineData(0x90000, 0x149dd94f8)] // replay manager
    [InlineData(0xa0000, 0x149dd94f8)] // wrong race state class
    public void NativeReaderRejectsUnrelatedOwnersAndReplay(long address, long replacement)
    {
        var memory = new Memory();memory.Put((ulong)address, (ulong)replacement);
        Assert.Throws<IOException>(() => ACRStageMemory.Read(memory.Read, Memory.Image));
    }

    [Theory]
    [InlineData(0x30040, 2)] [InlineData(0xa0120, 13)]
    public void NativeReaderRejectsInvalidPlayerCountOrPhase(long address, int value)
    {
        var memory = new Memory();memory.Put((ulong)address, value);
        Assert.Throws<IOException>(() => ACRStageMemory.Read(memory.Read, Memory.Image));
    }

    [Fact]
    public void NativeReaderRejectsInvalidCollectionAndMidReadSceneChange()
    {
        var memory = new Memory();memory.Array(0x60290, 0x80000, 2, 1);
        Assert.Throws<IOException>(() => ACRStageMemory.Read(memory.Read, Memory.Image));
        foreach (var address in new[] { Memory.Image + ACRVehicleMemory.WorldRva, 0xa0120UL })
        {
            memory = new Memory();var count = 0;
            byte[] Read(ulong at, int size)
            {
                var bytes = memory.Read(at, size);
                if (at == address && ++count == 2) bytes[0] ^= 8;
                return bytes;
            }
            Assert.Throws<IOException>(() => ACRStageMemory.Read(Read, Memory.Image));
        }
    }

    private sealed class Memory
    {
        public const ulong Image = 0x140000000;
        private readonly Dictionary<ulong, byte[]> values = new();
        public Memory()
        {
            foreach (var (address, value) in new (ulong, ulong)[]
            {
                (Image + ACRVehicleMemory.WorldRva, 0x20000), (0x20228, 0x30000),
                (0x30038, 0x40000), (0x40000, 0x50000), (0x50030, 0x60000),
                (0x602b0, 0x70000), (0x80000, 0x90000), (0x90000, Image + 0x9dd8c98),
                (0x900a0, 0x60000), (0x90200, 0xa0000), (0xa0000, Image + 0x9dd5da0),
                (0xa00a0, 0x70000)
            }) Put(address, value);
            Put(0x30040, 1);Array(0x60280, 0, 0, 0);Array(0x60290, 0x80000, 1, 1);
            Put(0xa0110, 2);Put(0xa0120, 4);
        }
        public void Put(ulong address, ulong value) => values[address] = BitConverter.GetBytes(value);
        public void Put(ulong address, int value) => values[address] = BitConverter.GetBytes(value);
        public void Array(ulong address, ulong pointer, int count, int capacity) =>
            values[address] = [..BitConverter.GetBytes(pointer), ..BitConverter.GetBytes(count), ..BitConverter.GetBytes(capacity)];
        public byte[] Read(ulong address, int count)
        {
            if (!values.TryGetValue(address, out var bytes) || bytes.Length != count)
                throw new IOException("Unavailable memory");
            return (byte[])bytes.Clone();
        }
    }
}
