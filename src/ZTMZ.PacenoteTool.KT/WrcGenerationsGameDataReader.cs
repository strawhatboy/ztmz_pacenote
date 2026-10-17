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
    private long nextMetadataRead, nextDiscovery, lifetime, runGeneration;
    private float? pendingFinish;
    private float rawLapTime;
    private readonly KtMechanicalDamageEvents damageEvents = new();
    private int? livePenaltyMilliseconds;
    private float? publishedFinishTime;
    private int invalidSilentSessionTicks;
    private string pendingIdentity = string.Empty;
    private string? preparedIdentity;
    private (string Identity, ulong Race, ulong Timer)? retirementAttempt;
    private bool retirementPublished;
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
            if (value != GameState.Racing) damageEvents.Reset();
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
    public override event Action<CarDamageEvent>? onCarDamaged;

    public override void onNewUdpMessage(byte[] oldMsg, byte[] newMsg)
    {
        if (!WrcGenerationsTelemetry.TryDecode(newMsg, out var next)) return;
        lock (dataLock) ProcessPacket(oldMsg, newMsg, next);
    }

    protected override void UpdateStateForUdpSilence() => CheckTelemetrySilence();

    internal void CheckTelemetrySilence()
    {
        lock (dataLock)
        {
            if (state is not (GameState.Racing or GameState.AdHocRaceBegin or GameState.Paused)) return;
            if (_timerCount < 2) ++_timerCount;
            if (_timerCount < 2) return;
            // Silence alone also happens on race exit or transport failure.
            // Only a live, matching, frozen race timer establishes pause.
            var current = metadata?.Read();
            if (TryPublishRetirement(current)) return;
            if (current?.Identity == preparedIdentity && current?.Timing is { } timing &&
                MathF.Abs(timing.RawLapMilliseconds / 1000f - rawLapTime) <= 0.5f)
            {
                invalidSilentSessionTicks = 0;
                if (state != GameState.Paused)
                    Log.Debug("WRCG telemetry suspended with a valid frozen race timer; preserving session");
                GameState = GameState.Paused;
                return;
            }
            // Tolerate torn reads. A different identity is immediate scene-exit
            // evidence; missing timing must persist before invalidating a session.
            if (current is not null && current.Identity != preparedIdentity || ++invalidSilentSessionTicks >= 10)
            {
                Log.Info("WRCG silent session is no longer verifiable; clearing active state without publishing a finish");
                ResetResult();
                snapshot = null;
                preparedIdentity = null;
                GameState = GameState.Unknown;
            }
        }
    }

    private void ProcessPacket(byte[] oldMsg, byte[] newMsg, GameData next)
    {
        if (isInitialized && metadata is null) BeginMetadataDiscovery();
        // Discovery is off the UI/UDP/game thread. Wait for identity before the
        // first lifecycle event so the core does not prepare an unnamed replay.
        if (metadataLoading) return;
        var raw = next.LapTime;
        var previousRaw = rawLapTime;
        var restarting = raw < previousRaw - 1 || (raw <= 0 && previousRaw > 0);
        if (restarting && !retirementPublished)
        {
            // Some end screens zero UDP clocks. A verified current failure
            // takes precedence; an actual restart has cleared native flags.
            RefreshMetadata(true);
            if (TryPublishRetirement(snapshot)) return;
        }
        if (restarting && !retirementPublished) ResetResult();
        RefreshMetadata(restarting || state is GameState.Unknown or GameState.RaceBegin);
        if (retirementPublished)
        {
            // Results may continue sending frozen UDP packets. Do not reopen
            // the race or turn an already reported retirement into a finish.
            if (snapshot?.NativeResult?.Pending != true && snapshot?.Identity == preparedIdentity) return;
            if (snapshot is null) return;
            ResetResult();
            state = GameState.Unknown;
        }
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
        invalidSilentSessionTicks = 0;
        rawLapTime = raw;
        UpdateLivePenalty(raw);
        next.LapTime = publishedFinishTime ?? (raw > 0 ? raw + (livePenaltyMilliseconds ?? 0) / 1000f : raw);
        next = KtShiftLights.ApplyWarning(next, snapshot?.ShiftWarningRpm ?? 0);
        // Inputs must be sampled per packet, not with the 200 ms name/result cache.
        if (snapshot is not null && metadata?.ReadHandBrake() is { } handBrake &&
            float.IsFinite(handBrake) && handBrake is >= 0 and <= 1)
        {
            next.HandBrake = handBrake;
            next.HandBrakeValid = true;
        }
        // Read damage on each UDP packet, independently of the metadata cache.
        // The live selected timer must still match the cached session identity.
        next.CarDamage = snapshot?.VehicleTimerAddress is { } damageTimer
            ? metadata?.ReadDamage(damageTimer) : null;
        next.Hybrid = snapshot?.VehicleTimerAddress is { } hybridTimer
            ? metadata?.ReadHybrid(hybridTimer) : null;
        LastGameData = CurrentGameData;
        CurrentGameData = next;
        if (TryPublishRetirement(snapshot))
        {
            onNewGameData?.Invoke(LastGameData, CurrentGameData);
            return;
        }
        // A failed native result at late attachment is not a fresh retirement,
        // but it must not be reinterpreted as a successful UDP finish either.
        if (snapshot?.NativeResult?.Retired == true)
        {
            onNewGameData?.Invoke(LastGameData, CurrentGameData);
            return;
        }
        if (raw > 0 && snapshot is { NativeResult: { Pending: true, TimerStarted: true } native, Timing: { } timing } observed &&
            timing.TimerAddress == native.TimerAddress && MathF.Abs(timing.RawLapMilliseconds / 1000f - raw) <= 0.5f)
            retirementAttempt = (observed.Identity, native.RaceAddress, native.TimerAddress);
        if (WrcGenerationsTelemetry.TryGetFinishTime(newMsg, out var finishTime))
        {
            damageEvents.Reset();
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
            onNewGameData?.Invoke(LastGameData, CurrentGameData);
            return;
        }
        // A first packet in a running stage must pass through AdHocRaceBegin so
        // the shared core prepares audio with the identity read above.
        if (raw > 0)
        {
            if (GameState == GameState.Unknown)
            {
                lastProgress = clock();
                GameState = GameState.AdHocRaceBegin;
            }
            else if (raw != previousRaw)
            {
                lastProgress = clock();
                // WRCG corrects the final clock backwards by a few milliseconds.
                // That must not be interpreted as a new race.
                GameState = restarting ? GameState.AdHocRaceBegin : GameState.Racing;
            }
            else if (clock() - lastProgress > 500) GameState = GameState.Paused;
        }
        else if (next.LapDistance <= 0) GameState = GameState.RaceBegin;
        foreach (var damage in damageEvents.Update(next.CarDamage,
            snapshot is null ? null : $"{snapshot.Identity}:{snapshot.Timing?.TimerAddress:x}", raw, GameState, restarting))
            onCarDamaged?.Invoke(damage);
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
            metadataLoading = false; nextDiscovery = nextMetadataRead = 0;
            LastGameData = CurrentGameData = default;
            rawLapTime = 0;
            GameState = GameState.Unknown;
        }
    }

    private void BeginMetadataDiscovery()
    {
        if (!OperatingSystem.IsWindows()) return;
        _ = DiscoverMetadataAsync(() =>
        {
            if (!OperatingSystem.IsWindows()) return null;
            using var process = System.Diagnostics.Process.GetProcessesByName("WRCG").FirstOrDefault();
            return process is null ? null : new WrcGenerationsMemorySession(process);
        });
    }

    internal Task DiscoverMetadataAsync(Func<IWrcGenerationsSessionSource?> create)
    {
        lock (dataLock)
        {
            if (metadata is not null || metadataLoading || clock() < nextDiscovery) return Task.CompletedTask;
            metadataLoading = true;
            var generation = lifetime;
            return Task.Run(() =>
            {
                IWrcGenerationsSessionSource? source = null;
                try { source = create(); }
                catch (Exception error) { Log.Warn(error, "WRCG session metadata unavailable; retrying in 5 seconds; UDP and live pacenotes remain available"); }
                lock (dataLock)
                {
                    if (generation != lifetime) { source?.Dispose(); return; }
                    metadata = source;
                    metadataLoading = false;
                    if (source is null) nextDiscovery = clock() + 5000;
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
        damageEvents.Reset();
        ++runGeneration;
        pendingFinish = null;
        resultArmed = false;
        livePenaltyMilliseconds = null;
        publishedFinishTime = null;
        retirementAttempt = null;
        retirementPublished = false;
    }

    private bool TryPublishRetirement(WrcGenerationsSessionSnapshot? current)
    {
        if (retirementAttempt is { } armed && current is not null &&
            (current.Identity != armed.Identity ||
             current.NativeResult is { } live && (!live.TimerStarted || live.RaceAddress != armed.Race || live.TimerAddress != armed.Timer) ||
             current.Timing is { } timing && timing.RawLapMilliseconds / 1000f < rawLapTime - 1))
            retirementAttempt = null;
        if (state is not (GameState.Racing or GameState.AdHocRaceBegin or GameState.Paused) ||
            pendingFinish is not null || retirementPublished || retirementAttempt is not { } attempt ||
            current is not { NativeResult: { Retired: true } result } ||
            current.Identity != preparedIdentity || current.Identity != attempt.Identity ||
            result.RaceAddress != attempt.Race || result.TimerAddress != attempt.Timer) return false;
        var previous = state;
        ResetResult();
        snapshot = current;
        retirementPublished = true;
        state = GameState.RaceEnd;
        Log.Info("WRCG retirement: verified current timer and player-profile failure result; track={0}", current.Track);
        onGameStateChanged?.Invoke(new()
        {
            LastGameState = previous, NewGameState = state,
            Parameters = new() { [GameStateRaceEndProperty.FINISH_STATE] = GameStateRaceEnd.Retired }
        });
        return true;
    }

    private void UpdateLivePenalty(float raw)
    {
        // Metadata is sampled every 200 ms. Reject clocks from another run;
        // retain an accepted cumulative value through transient read failures.
        if (snapshot?.Timing is not { } timing ||
            MathF.Abs(timing.RawLapMilliseconds / 1000f - raw) > 0.5f) return;
        if (livePenaltyMilliseconds != timing.PenaltyMilliseconds)
            Log.Info("WRCG cumulative penalty: {0} ms, raw lap time={1}s", timing.PenaltyMilliseconds, raw);
        livePenaltyMilliseconds = timing.PenaltyMilliseconds;
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
        var official = penalty.HasValue;
        if (!official && livePenaltyMilliseconds is { } milliseconds)
        {
            penalty = milliseconds / 1000f;
            time += penalty.Value;
        }
        publishedFinishTime = time;
        var finalData = CurrentGameData;
        finalData.LapTime = time;
        CurrentGameData = finalData;
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
            official ? "player-profile (including penalties)" :
            penalty.HasValue ? "UDP plus live cumulative penalty" : "UDP raw time (penalties unavailable)");
        onGameStateChanged?.Invoke(new() { LastGameState = previous, NewGameState = state, Parameters = parameters });
    }
}
