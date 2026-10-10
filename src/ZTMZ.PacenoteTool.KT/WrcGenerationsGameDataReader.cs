using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

/// <summary>Uses the shared UDP transport; lifecycle evidence is supplied separately.</summary>
public sealed class WrcGenerationsGameDataReader : UdpGameDataReader, IGameLivePacenoteSource
{
    private WrcGenerationsLiveSession? live;
    public bool BridgeConnected => live?.Connected == true;
    public string? BridgeError => live?.Error;
    public event Action<LivePacenoteEvent>? onLivePacenote;
    private long lastProgress;
    private readonly Func<long> clock;
    private readonly object sessionLock = new();
    private readonly object dataLock = new();
    private IWrcGenerationsSessionSource? metadata;
    private WrcGenerationsSessionSnapshot? snapshot;
    private bool metadataLoading, resultArmed;
    private int metadataPid;
    private long nextMetadataRead, nextDiscovery, lifetime, runGeneration;
    private float? pendingFinish;
    private string pendingIdentity = string.Empty;
    private string? preparedIdentity;
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
    public WrcGenerationsGameDataReader(Func<long>? monotonicMilliseconds = null) => clock = monotonicMilliseconds ?? (() => Environment.TickCount64);
    internal WrcGenerationsGameDataReader(IWrcGenerationsSessionSource source, Func<long>? monotonicMilliseconds = null)
        : this(monotonicMilliseconds) => metadata = source;

    public override bool Initialize(IGame game)
    {
        if (!base.Initialize(game)) return false;
        BeginMetadataDiscovery();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessesByName("WRCG").FirstOrDefault();
                if (process?.MainModule?.FileName is { } executable) EnsureBridgeSession(process.Id, Path.GetDirectoryName(executable)!);
            }
            catch (Exception error) { NLog.LogManager.GetCurrentClassLogger().Warn(error, "WRCG bridge discovery failed; UDP remains available"); }
        }
        return true;
    }

    internal Task EnsureBridgeSession(int pid, string directory)
    {
        lock (sessionLock)
        {
            if (live?.Error is not null) { live.Dispose(); live = null; }
            live ??= new WrcGenerationsLiveSession(pid, directory, evt =>
            {
                NLog.LogManager.GetCurrentClassLogger().Debug("WRCG live dispatch: resource={0}, state={1}, lapTime={2}, distance={3}",
                    evt.SourceResourceName, GameState, CurrentGameData.LapTime, CurrentGameData.LapDistance);
                onLivePacenote?.Invoke(evt);
            });
            return live.Ready.Task;
        }
    }
    private GameState state;
    public override GameState GameState
    {
        get => state;
        set
        {
            if (state == value) return;
            var previous = state;
            state = value;
            onGameStateChanged?.Invoke(new() { LastGameState = previous, NewGameState = value });
        }
    }
    public override GameData LastGameData { get; set; }
    public override GameData CurrentGameData { get; set; }
    public override string TrackName => Volatile.Read(ref snapshot)?.Track ?? string.Empty;
    public override string CarName => Volatile.Read(ref snapshot)?.Car ?? string.Empty;
    public override string CarClass => Volatile.Read(ref snapshot)?.CarClass ?? string.Empty;
    public override event Action<GameData, GameData>? onNewGameData;
    public override event Action<GameStateChangeEvent>? onGameStateChanged;

    public override void onNewUdpMessage(byte[] oldMsg, byte[] newMsg)
    {
        if (!WrcGenerationsTelemetry.TryDecode(newMsg, out var next)) return;
        lock (dataLock) ProcessPacket(oldMsg, newMsg, next);
    }

    private void ProcessPacket(byte[] oldMsg, byte[] newMsg, GameData next)
    {
        if (isInitialized && metadata is null) BeginMetadataDiscovery();
        // Discovery is off the UI/UDP/game thread. Wait for identity before the
        // first lifecycle event so the core does not prepare an unnamed replay.
        if (metadataLoading) return;
        var restarting = next.LapTime < CurrentGameData.LapTime - 1 ||
            (next.LapTime <= 0 && CurrentGameData.LapTime > 0);
        if (restarting) ResetResult();
        RefreshMetadata(restarting || state is GameState.Unknown or GameState.RaceBegin);
        if (snapshot is { } current && preparedIdentity != current.Identity)
        {
            // Also recover if metadata was briefly unavailable at initial attach.
            // This must happen before lifecycle callbacks capture the names.
            if (state != GameState.Unknown)
            {
                ResetResult();
                resultArmed = !current.ResultReady;
                state = GameState.Unknown;
            }
            preparedIdentity = current.Identity;
        }
        base.onNewUdpMessage(oldMsg, newMsg);
        LastGameData = CurrentGameData;
        CurrentGameData = next;
        if (WrcGenerationsTelemetry.TryGetFinishTime(newMsg, out var finishTime))
        {
            if (state != GameState.RaceEnd && pendingFinish is null)
            {
                RefreshMetadata(true);
                pendingFinish = finishTime;
                pendingIdentity = snapshot?.Identity ?? string.Empty;
                if (!TryPublishOfficialResult())
                {
                    if (metadata is null) PublishFinish(finishTime, null);
                    else _ = AwaitFinalResultAsync(runGeneration, lifetime);
                }
            }
            onNewGameData?.Invoke(LastGameData, next);
            return;
        }
        // A first packet in a running stage must pass through AdHocRaceBegin so
        // the shared core prepares audio with the identity read above.
        if (next.LapTime > 0)
        {
            if (GameState == GameState.Unknown)
            {
                lastProgress = clock();
                GameState = GameState.AdHocRaceBegin;
            }
            else if (next.LapTime != LastGameData.LapTime)
            {
                lastProgress = clock();
                // WRCG corrects the final clock backwards by a few milliseconds.
                // That must not be interpreted as a new race.
                GameState = next.LapTime < LastGameData.LapTime - 1 ? GameState.AdHocRaceBegin : GameState.Racing;
            }
            else if (clock() - lastProgress > 500) GameState = GameState.Paused;
        }
        else if (next.LapDistance <= 0) GameState = GameState.RaceBegin;
        onNewGameData?.Invoke(LastGameData, next);
    }

    public override void Uninitialize(IGame game)
    {
        base.Uninitialize(game);
        lock (sessionLock) { live?.Dispose(); live = null; }
        lock (dataLock)
        {
            ++lifetime;
            ResetResult();
            metadata?.Dispose(); metadata = null; snapshot = null;
            preparedIdentity = null;
            metadataLoading = false; metadataPid = 0; nextDiscovery = nextMetadataRead = 0;
            LastGameData = CurrentGameData = default;
            GameState = GameState.Unknown;
        }
    }

    private void BeginMetadataDiscovery()
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (dataLock)
        {
            if (metadata is not null || metadataLoading || Environment.TickCount64 < nextDiscovery) return;
            nextDiscovery = Environment.TickCount64 + 1000;
            var process = System.Diagnostics.Process.GetProcessesByName("WRCG").FirstOrDefault();
            if (process is null) return;
            if (process.Id == metadataPid) { process.Dispose(); return; }
            metadataPid = process.Id;
            metadataLoading = true;
            var generation = lifetime;
            _ = Task.Run(() =>
            {
                IWrcGenerationsSessionSource? source = null;
                try { source = new WrcGenerationsMemorySession(process); }
                catch (Exception error) { Log.Warn(error, "WRCG session metadata unavailable; UDP and live pacenotes remain available"); }
                finally { process.Dispose(); }
                lock (dataLock)
                {
                    if (generation != lifetime) { source?.Dispose(); return; }
                    metadata = source;
                    metadataLoading = false;
                    if (source is not null) Log.Info("WRCG read-only session metadata attached");
                }
            });
        }
    }

    private void RefreshMetadata(bool force = false)
    {
        if (metadata is null || (!force && clock() < nextMetadataRead)) return;
        nextMetadataRead = clock() + 200;
        var current = metadata.Read();
        if (current is null)
        {
            // Never retain a previous scene's identity through invalid pointers.
            snapshot = null;
            return;
        }
        if (snapshot?.Identity != current.Identity)
        {
            if (snapshot is not null) ResetResult();
            Log.Info("WRCG session: track={0}, car={1}, class={2}", current.Track, current.Car, current.CarClass);
        }
        snapshot = current;
        if (!current.ResultReady) resultArmed = true;
    }

    private void ResetResult()
    {
        ++runGeneration;
        pendingFinish = null;
        resultArmed = false;
    }

    private bool TryPublishOfficialResult()
    {
        if (pendingFinish is not { } raw || !resultArmed || snapshot is not { ResultReady: true } result ||
            result.Identity != pendingIdentity || MathF.Abs(result.RaceMilliseconds / 1000f - raw) > 0.002f)
            return false;
        PublishFinish(result.ResultMilliseconds / 1000f, (result.ResultMilliseconds - result.RaceMilliseconds) / 1000f);
        return true;
    }

    private async Task AwaitFinalResultAsync(long generation, long owner)
    {
        try
        {
            // The game fills WrcPlayerProfile statistics just after crossing the
            // line. Do not save the raw UDP clock as a penalty-inclusive result.
            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(50).ConfigureAwait(false);
                lock (dataLock)
                {
                    if (owner != lifetime || generation != runGeneration || pendingFinish is null) return;
                    RefreshMetadata(true);
                    if (generation != runGeneration || pendingFinish is null) return;
                    if (TryPublishOfficialResult()) return;
                }
            }
            lock (dataLock)
                if (owner == lifetime && generation == runGeneration && pendingFinish is { } raw)
                    PublishFinish(raw, null);
        }
        catch (Exception error) { Log.Error(error, "WRCG final result publication failed"); }
    }

    private void PublishFinish(float time, float? penalty)
    {
        var previous = state;
        state = GameState.RaceEnd;
        pendingFinish = null;
        var parameters = new Dictionary<string, object>
        {
            [GameStateRaceEndProperty.FINISH_STATE] = GameStateRaceEnd.Normal,
            [GameStateRaceEndProperty.FINISH_TIME] = time
        };
        if (penalty is { } value) parameters[GameStateRaceEndProperty.FINISH_TIME_PANALTY] = value;
        Log.Info("WRCG finish result: time={0}s, penalty={1}, source={2}", time,
            penalty?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown",
            penalty.HasValue ? "player-profile (including penalties)" : "UDP raw time (penalties unavailable)");
        onGameStateChanged?.Invoke(new() { LastGameState = previous, NewGameState = state, Parameters = parameters });
    }
}
