using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Codemasters;
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
}
