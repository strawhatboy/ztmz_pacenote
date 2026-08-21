using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
using ZTMZ.PacenoteTool.Core;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class GameActivityMonitoringTest
{
    [Fact]
    public void MatcherRecognizesWindowsExecutableInsideProtonCommandLine()
    {
        var process = Snapshot(
            42,
            "pressure-vessel",
            ["wine64", "/games/DiRT Rally/drt.exe"]);

        Assert.True(ProtonGameProcessMatcher.Matches(new DirtRally(), process));
        Assert.False(ProtonGameProcessMatcher.Matches(new DirtRally2(), process));
    }

    [Fact]
    public void MatcherRecognizesSteamIdentityFromEnvironmentOrCompatdataPath()
    {
        var game = new DirtRally2();
        var environmentProcess = Snapshot(
            51,
            "wineserver",
            ["wineserver"],
            new Dictionary<string, string> { ["SteamAppId"] = "690790" });
        var compatdataProcess = Snapshot(
            52,
            "proton",
            ["/steamapps/compatdata/690790/pfx/drive_c/game.exe"]);

        Assert.True(ProtonGameProcessMatcher.Matches(game, environmentProcess));
        Assert.True(ProtonGameProcessMatcher.Matches(game, compatdataProcess));
    }

    [Fact]
    public void DetectorRaisesOnlyFirstStartAndFinalStop()
    {
        var game = new DirtRally();
        var detector = new GameActivityDetector();
        var running = Snapshot(61, "drt.exe", ["drt.exe"]);

        var started = Assert.Single(detector.Update([game], [running]));
        Assert.Equal(GameActivityChangeKind.Started, started.Kind);
        Assert.Equal(61, started.Activity.ProcessId);
        Assert.Empty(detector.Update([game], [running]));

        var stopped = Assert.Single(detector.Update([game], []));
        Assert.Equal(GameActivityChangeKind.Stopped, stopped.Kind);
        Assert.Same(game, stopped.Activity.Game);
    }

    [Fact]
    public void SystemSnapshotIncludesCurrentProcess()
    {
        var currentProcessId = Environment.ProcessId;

        var snapshots = new SystemGameProcessSnapshotProvider().Capture();

        Assert.Contains(snapshots, process => process.ProcessId == currentProcessId);
    }

    [Fact]
    public async Task PollingMonitorDetectsAlreadyRunningGameAndItsExit()
    {
        var game = new DirtRally();
        var snapshots = new MutableSnapshotProvider
        {
            Processes = [Snapshot(71, "drt", ["drt"])]
        };
        await using var monitor = new PollingGameActivityMonitor(
            snapshots,
            TimeSpan.FromMilliseconds(10));
        var started = new TaskCompletionSource<GameActivity>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<GameActivity>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.GameStarted += activity => started.TrySetResult(activity);
        monitor.GameStopped += activity => stopped.TrySetResult(activity);

        monitor.Start([game]);
        Assert.Same(game, (await started.Task.WaitAsync(TimeSpan.FromSeconds(2))).Game);

        snapshots.Processes = [];
        Assert.Same(game, (await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2))).Game);
    }

    [Fact]
    public async Task SessionCoordinatorActivatesInitialGameAndFallsBackWhenCurrentGameStops()
    {
        var firstGame = new DirtRally();
        var secondGame = new DirtRally2();
        var monitor = new TestGameActivityMonitor(firstGame);
        var sessions = new TestGameSessionActivator();
        await using var coordinator = new GameActivitySessionCoordinator(
            monitor,
            sessions,
            [firstGame, secondGame]);
        var activityChanges = new List<GameActivitySessionChange>();
        coordinator.ActivityChanged += (_, change) => activityChanges.Add(change);

        coordinator.Start();

        Assert.Same(firstGame, sessions.CurrentGame);
        Assert.True(firstGame.IsRunning);

        monitor.RaiseStarted(secondGame);
        Assert.Same(secondGame, sessions.CurrentGame);

        monitor.RaiseStopped(secondGame);
        Assert.Same(firstGame, sessions.CurrentGame);
        Assert.True(firstGame.IsRunning);
        Assert.False(secondGame.IsRunning);
        var fallbackChange = Assert.Single(activityChanges, change =>
            change.Kind == GameActivityChangeKind.Stopped &&
            ReferenceEquals(change.Activity.Game, secondGame));
        Assert.True(fallbackChange.SessionActivated);
        Assert.Same(firstGame, fallbackChange.CurrentGame);
        Assert.Same(firstGame, fallbackChange.CurrentActivity?.Game);

        await coordinator.StopAsync();
        Assert.Null(sessions.CurrentGame);
        Assert.False(firstGame.IsRunning);
    }

    private static GameProcessSnapshot Snapshot(
        int processId,
        string processName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        return new GameProcessSnapshot(
            processId,
            processName,
            arguments,
            environment ?? new Dictionary<string, string>());
    }

    private sealed class MutableSnapshotProvider : IGameProcessSnapshotProvider
    {
        private IReadOnlyList<GameProcessSnapshot> _processes = [];

        public IReadOnlyList<GameProcessSnapshot> Processes
        {
            get => Volatile.Read(ref _processes);
            set => Volatile.Write(ref _processes, value);
        }

        public IReadOnlyList<GameProcessSnapshot> Capture() => Processes;
    }

    private sealed class TestGameActivityMonitor : IGameActivityMonitor
    {
        private readonly List<GameActivity> _activeGames = [];
        private readonly IGame? _initialGame;

        public TestGameActivityMonitor(IGame? initialGame = null)
        {
            _initialGame = initialGame;
        }

        public event Action<GameActivity>? GameStarted;
        public event Action<GameActivity>? GameStopped;

        public IReadOnlyCollection<GameActivity> ActiveGames => _activeGames;

        public void Start(IReadOnlyList<IGame> games)
        {
            if (_initialGame != null)
            {
                RaiseStarted(_initialGame);
            }
        }

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseStarted(IGame game)
        {
            var activity = new GameActivity(game, _activeGames.Count + 1, game.Executable);
            _activeGames.Add(activity);
            GameStarted?.Invoke(activity);
        }

        public void RaiseStopped(IGame game)
        {
            var activity = _activeGames.Single(candidate =>
                ReferenceEquals(candidate.Game, game));
            _activeGames.Remove(activity);
            GameStopped?.Invoke(activity);
        }
    }

    private sealed class TestGameSessionActivator : IGameSessionActivator
    {
        public IGame? CurrentGame { get; private set; }

        public bool ActivateGame(IGame game)
        {
            CurrentGame = game;
            return true;
        }

        public void DeactivateCurrentGame() => CurrentGame = null;
    }
}
