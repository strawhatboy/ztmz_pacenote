using System.Diagnostics;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.Script;

namespace ZTMZ.PacenoteTool.KT;

public sealed class Wrc9GameDataReader : IGameDataReader
    , IGameLivePacenoteSource
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private Wrc9LiveSession? live;
    private Wrc9Snapshot? snapshot;
    private TaskCompletionSource listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GameState state;
    public bool BridgeConnected => live?.Connected == true;
    public string? Error { get; private set; }
    internal Task Listening => listening.Task;
    internal Task BridgeReady => live?.Ready.Task ?? Task.FromException(new InvalidOperationException("WRC9 receiver is not initialized"));
    public GameState GameState
    {
        get => state;
        set { if (state == value) return; var old = state; state = value; onGameStateChanged?.Invoke(new() { LastGameState = old, NewGameState = value }); }
    }
    public GameData LastGameData { get; set; }
    public GameData CurrentGameData { get; set; }
    public string TrackName => snapshot?.Track ?? string.Empty;
    public string CarName => snapshot?.Car ?? string.Empty;
    public string CarClass => snapshot?.CarClass ?? string.Empty;
    public event Action<GameData, GameData>? onNewGameData;
    public event Action<bool>? onGameDataAvailabilityChanged;
    public event Action<GameStateChangeEvent>? onGameStateChanged;
    public event Action<LivePacenoteEvent>? onLivePacenote;
    public event Action<CarDamageEvent>? onCarDamaged;
    // Manual car-reset signals remain unverified.
    public event Action? onCarReset { add { } remove { } }

    public bool Initialize(IGame game)
    {
        if (!OperatingSystem.IsWindows()) return false;
        lock (gate)
        {
            if (cancellation is not null) return true;
            var lifetime = cancellation = new CancellationTokenSource();
            listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Error = null;
            var previousWorker = worker;
            worker = Task.Run(async () =>
            {
                if (!OperatingSystem.IsWindows()) return;
                Wrc9LiveSession? receiver = null;
                try
                {
                    // Release the previous pipe before a rapid game re-selection
                    // starts another receiver for the same process.
                    if (previousWorker is not null) await previousWorker;
                    lifetime.Token.ThrowIfCancellationRequested();
                    while (!lifetime.IsCancellationRequested)
                    {
                        // Selecting a game before launching it is normal. Keep the
                        // receiver lifetime alive instead of caching a failed task.
                        using var process = await WaitForProcessAsync(lifetime.Token);
                        using var memory = await OpenSessionAsync(process, lifetime.Token);
                        if (memory is null) continue; // Launcher exited/replaced its process.
                        lifetime.Token.ThrowIfCancellationRequested();
                        receiver = new Wrc9LiveSession(process.Id, memory.Pacenotes,
                            ScriptResource.Instance.FilenameToIdDict.Keys.ToHashSet(StringComparer.Ordinal), evt =>
                            {
                                lock (gate)
                                    if (cancellation == lifetime && !lifetime.IsCancellationRequested && ((Wrc9Config)game.GameConfigurations[Wrc9Config.Name]).ControlsEnabled)
                                        onLivePacenote?.Invoke(evt);
                            });
                        lock (gate)
                        {
                            if (cancellation != lifetime || lifetime.IsCancellationRequested) return;
                            live = receiver; listening.TrySetResult();
                        }
                        var session = new Wrc9RaceSession();
                        var damageEvents = new Wrc9DamageEvents();
                        var available = false;
                        var vehicleTelemetryAvailable = false;
                        while (!lifetime.IsCancellationRequested && !process.HasExited)
                        {
                            var current = memory.Read();
                            var nextState = session.Update(current, Environment.TickCount64);
                            lock (gate)
                            {
                                if (cancellation != lifetime || lifetime.IsCancellationRequested) return;
                                snapshot = current;
                                if (available != (current is not null)) { available = current is not null; onGameDataAvailabilityChanged?.Invoke(available); }
                                LastGameData = CurrentGameData;
                                CurrentGameData = current?.Telemetry ?? new() { TimeStamp = DateTime.Now };
                                var hasVehicleTelemetry = CurrentGameData.MaxRPM > 0;
                                if (hasVehicleTelemetry != vehicleTelemetryAvailable)
                                {
                                    vehicleTelemetryAvailable = hasVehicleTelemetry;
                                    Log.Info("WRC9 vehicle telemetry {0}: speed={1:F1} km/h, gear={2}, RPM={3:F0}, maxRPM={4:F0}",
                                        hasVehicleTelemetry ? "available" : "unavailable", CurrentGameData.Speed,
                                        CurrentGameData.Gear, CurrentGameData.RPM, CurrentGameData.MaxRPM);
                                }
                                if (session.IsNewSession && nextState != GameState.RaceEnd) GameState = current!.RawMilliseconds > 0 ? GameState.AdHocRaceBegin : GameState.RaceBegin;
                                if (nextState == GameState.RaceEnd && state != GameState.RaceEnd)
                                {
                                    var oldState = state;
                                    state = GameState.RaceEnd;
                                    Log.Info("WRC9 verified finish: state={0}, raw={1} ms, penalty={2} ms, total={3} ms",
                                        session.FinishState, current!.RawMilliseconds, current.PenaltyMilliseconds, current.ResultMilliseconds);
                                    onGameStateChanged?.Invoke(new()
                                    {
                                        LastGameState = oldState, NewGameState = state,
                                        Parameters = session.EndParameters(current)
                                    });
                                }
                                else GameState = nextState;
                                if (current is not null) onNewGameData?.Invoke(LastGameData, CurrentGameData);
                                foreach (var damage in damageEvents.Update(current, nextState, session.IsNewSession)) onCarDamaged?.Invoke(damage);
                            }
                            await Task.Delay(50, lifetime.Token);
                        }
                        receiver.Dispose(); receiver = null;
                        lock (gate)
                        {
                            if (cancellation != lifetime || lifetime.IsCancellationRequested) return;
                            live = null; snapshot = null;
                            listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
                            GameState = GameState.Unknown;
                            onGameDataAvailabilityChanged?.Invoke(false);
                        }
                    }
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception error)
                {
                    lock (gate) if (cancellation == lifetime) { Error = error.Message; listening.TrySetException(error); }
                    Log.Error(error, "WRC9 session failed");
                }
                finally
                {
                    receiver?.Dispose();
                    lock (gate) if (cancellation == lifetime) { live = null; snapshot = null; GameState = GameState.Unknown; onGameDataAvailabilityChanged?.Invoke(false); }
                }
            });
            return true;
        }
    }
    private static async Task<Process> WaitForProcessAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var processes = Process.GetProcessesByName("WRC9");
            var selected = processes.FirstOrDefault();
            foreach (var other in processes.Skip(1)) other.Dispose();
            if (selected is not null)
            {
                try
                {
                    if (!selected.HasExited && selected.MainWindowHandle != IntPtr.Zero) return selected;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode is 299 or 6 or 87) { }
                selected.Dispose();
            }
            await Task.Delay(250, token);
        }
    }
    private static async Task<Wrc9MemorySession?> OpenSessionAsync(Process process, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited) return null;
            try
            {
                var modules = process.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (modules.Contains("physx64_s.dll") && modules.Contains("fmodstudio.dll"))
                    return new Wrc9MemorySession(process);
            }
            catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode is 299 or 6 or 87)
            {
                // Windows loader/module enumeration is not stable during startup.
                // Do not fault the lifetime task or repeatedly prompt the user.
                Log.Debug("Waiting for WRC9 PID {0} modules: Win32 {1}", process.Id, error.NativeErrorCode);
            }
            catch (InvalidOperationException) when (process.HasExited) { return null; }
            await Task.Delay(250, token);
        }
    }
    public void Uninitialize(IGame game)
    {
        lock (gate)
        {
            var old = cancellation; if (old is null) return;
            cancellation = null; old.Cancel(); listening.TrySetCanceled(); live = null; snapshot = null;
            GameState = GameState.Unknown; onGameDataAvailabilityChanged?.Invoke(false);
            _ = worker!.ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
        }
    }
}
