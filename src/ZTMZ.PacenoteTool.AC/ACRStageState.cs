using System.Buffers.Binary;
using System.IO;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

internal sealed record ACRStageState(ulong World, ulong Controller, ulong State, int RaceId, int Phase);

// Current-build reflection offsets and owner checks, independently captured at
// PreSemaphore -> Semaphore -> Running. Access requires the verified EXE hash.
internal static class ACRStageMemory
{
    internal static ACRStageState Read(Func<ulong, int, byte[]> read, ulong image)
    {
        var guards = new Dictionary<(ulong Address, int Count), byte[]>();
        byte[] Stable(ulong address, int count)
        {
            var bytes = read(address, count);
            if (bytes.Length != count) throw new IOException("Incomplete ACR stage read.");
            guards[(address, count)] = bytes;
            return bytes;
        }
        ulong Pointer(ulong address)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(Stable(address, 8));
            if (value < 0x10000 || value > 0x7fffffffffff || (value & 7) != 0)
                throw new IOException("ACR stage pointer unavailable.");
            return value;
        }
        var world = Pointer(image + ACRVehicleMemory.WorldRva);
        var instance = Pointer(world + 0x228);
        var players = Pointer(instance + 0x38);
        if (BitConverter.ToInt32(Stable(instance + 0x40, 4)) != 1)
            throw new IOException("ACR local player unavailable.");
        var player = Pointer(players);
        var controller = Pointer(player + 0x30);
        var playerState = Pointer(controller + 0x2b0);
        var managers = new HashSet<ulong>();
        foreach (var offset in new ulong[] { 0x280, 0x290 })
        {
            var header = Stable(controller + offset, 16);
            var array = BitConverter.ToUInt64(header);
            var count = BitConverter.ToInt32(header, 8);
            var capacity = BitConverter.ToInt32(header, 12);
            if (count < 0 || capacity < count || capacity > 1024 ||
                (count > 0 && (array < 0x10000 || array > 0x7fffffffffff || (array & 7) != 0)))
                throw new IOException("Invalid ACR controller components.");
            for (var i = 0; i < count; i++)
            {
                var component = Pointer(array + (ulong)i * 8);
                // Excludes the different RallyManagerReplay class.
                if (Pointer(component) == image + 0x9dd8c98 && Pointer(component + 0xa0) == controller)
                    managers.Add(component);
            }
        }
        if (managers.Count != 1) throw new IOException("ACR rally manager unavailable or ambiguous.");
        var state = Pointer(managers.Single() + 0x200);
        if (Pointer(state) != image + 0x9dd5da0 || Pointer(state + 0xa0) != playerState)
            throw new IOException("Invalid ACR race-state owner or type.");
        var raceId = BitConverter.ToInt32(Stable(state + 0x110, 4));
        var phase = BitConverter.ToInt32(Stable(state + 0x120, 4));
        if (phase is < 0 or > 12) throw new IOException("Unknown ACR race phase.");
        foreach (var (key, bytes) in guards)
            if (!read(key.Address, key.Count).AsSpan().SequenceEqual(bytes))
                throw new IOException("ACR scene or race phase changed during read.");
        return new(world, controller, state, raceId, phase);
    }
}

internal sealed class ACRStageEntry
{
    private ACRStageState? previous;
    private bool welcomeHandled;
    private (string Name, string Layout)? track;
    private int? welcomedRaceId;
    private long sequence;

    internal void Reset()
    {
        previous = null;
        welcomeHandled = false;
        track = null;
        welcomedRaceId = null;
    }

    internal LivePacenoteEvent? Observe(ACRStageState? state, string? trackName = null, string? trackLayout = null)
    {
        // An unavailable read is not a stage exit, pause, restart or retirement.
        if (state is null) return null;
        var old = previous;
        previous = state;
        // RaceId increments on a time-trial restart; the loaded scene and its
        // owned controller/state remain the same. Welcome once per loaded session.
        if (old is null || old.World != state.World || old.Controller != state.Controller || old.State != state.State)
        {
            welcomeHandled = false;
            track = null;
            welcomedRaceId = null;
        }
        var name = trackName?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            var currentTrack = (Name: name, Layout: trackLayout?.Trim() ?? string.Empty);
            // A different track/layout can reuse the native session objects.
            // Missing metadata is not a change. If we already welcomed this
            // attempt, a delayed metadata update must not produce a second cue.
            if (track.HasValue && track.Value != currentTrack && welcomedRaceId != state.RaceId)
                welcomeHandled = false;
            track = currentTrack;
        }
        // Attaching after the start must not defer a welcome until a restart.
        if (state.Phase >= 5) welcomeHandled = true;
        // StartSequence is already the loaded stage/HUD; PreSemaphore is the
        // subsequent wait. They are one entry, including attachment at either.
        if (welcomeHandled || state.Phase is not (3 or 4) || state.RaceId < 0) return null;
        welcomeHandled = true;
        welcomedRaceId = state.RaceId;
        return new LivePacenoteEvent
        {
            Id = $"acr-stage-entry-{++sequence}",
            StageId = state.RaceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SourceToken = state.Phase == 3 ? "StartSequence" : "PreSemaphore",
            SystemSoundToken = Constants.SYSTEM_START_STAGE,
            SystemSoundSequential = true
        };
    }
}
