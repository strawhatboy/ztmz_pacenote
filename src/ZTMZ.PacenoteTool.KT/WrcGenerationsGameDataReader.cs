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
    public WrcGenerationsGameDataReader(Func<long>? monotonicMilliseconds = null) => clock = monotonicMilliseconds ?? (() => Environment.TickCount64);

    public override bool Initialize(IGame game)
    {
        if (!base.Initialize(game)) return false;
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
            live ??= new WrcGenerationsLiveSession(pid, directory, evt => onLivePacenote?.Invoke(evt));
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
    public override string TrackName => string.Empty;
    public override string CarName => string.Empty;
    public override string CarClass => string.Empty;
    public override event Action<GameData, GameData>? onNewGameData;
    public override event Action<GameStateChangeEvent>? onGameStateChanged;

    public override void onNewUdpMessage(byte[] oldMsg, byte[] newMsg)
    {
        if (!WrcGenerationsTelemetry.TryDecode(newMsg, out var next)) return;
        base.onNewUdpMessage(oldMsg, newMsg);
        LastGameData = CurrentGameData;
        CurrentGameData = next;
        // A first packet in a running stage must pass through AdHocRaceBegin so
        // the shared core prepares audio. Stage identity/finish are not guessed.
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
        LastGameData = CurrentGameData = default;
        GameState = GameState.Unknown;
    }
}
