#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base.Game;

public sealed record GameProcessSnapshot(
    int ProcessId,
    string ProcessName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);

public interface IGameProcessSnapshotProvider
{
    IReadOnlyList<GameProcessSnapshot> Capture();
}

public sealed record GameActivity(IGame Game, int ProcessId, string ProcessName);

public enum GameActivityChangeKind
{
    Started,
    Stopped
}

public sealed record GameActivityChange(GameActivityChangeKind Kind, GameActivity Activity);

public interface IGameActivityMonitor : IAsyncDisposable
{
    event Action<GameActivity>? GameStarted;
    event Action<GameActivity>? GameStopped;

    IReadOnlyCollection<GameActivity> ActiveGames { get; }

    void Start(IReadOnlyList<IGame> games);
    Task StopAsync();
}

public static class ProtonGameProcessMatcher
{
    private static readonly string[] SteamApplicationIdVariables =
    [
        "SteamAppId",
        "SteamGameId",
        "STEAM_COMPAT_APP_ID"
    ];

    public static bool Matches(IGame game, GameProcessSnapshot process)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(process);

        var expectedExecutable = NormalizeExecutable(game.Executable);
        if (!string.IsNullOrEmpty(expectedExecutable) &&
            EnumerateExecutableCandidates(process).Any(candidate =>
                string.Equals(
                    NormalizeExecutable(candidate),
                    expectedExecutable,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (game is not ISteamGameIdentity steamGame)
        {
            return false;
        }

        var expectedAppId = steamGame.SteamApplicationId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        foreach (var variable in SteamApplicationIdVariables)
        {
            var value = process.Environment.FirstOrDefault(pair =>
                string.Equals(pair.Key, variable, StringComparison.OrdinalIgnoreCase)).Value;
            if (string.Equals(value, expectedAppId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return process.Arguments.Any(argument =>
            ContainsPathSegment(argument, "compatdata", expectedAppId) ||
            ContainsAssignment(argument, "SteamAppId", expectedAppId) ||
            ContainsAssignment(argument, "SteamGameId", expectedAppId) ||
            ContainsAssignment(argument, "STEAM_COMPAT_APP_ID", expectedAppId));
    }

    private static IEnumerable<string> EnumerateExecutableCandidates(GameProcessSnapshot process)
    {
        yield return process.ProcessName;
        foreach (var argument in process.Arguments)
        {
            yield return argument;
        }
    }

    private static string NormalizeExecutable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Trim('"', '\'');
        var separator = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
        if (separator >= 0 && separator + 1 < normalized.Length)
        {
            normalized = normalized[(separator + 1)..];
        }

        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }

    private static bool ContainsPathSegment(string value, string segment, string appId)
    {
        var normalized = value.Replace('\\', '/');
        return normalized.Contains(
            $"/{segment}/{appId}/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAssignment(string value, string name, string expectedValue)
    {
        return string.Equals(
            value.Trim(),
            $"{name}={expectedValue}",
            StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class GameActivityDetector
{
    private readonly Dictionary<IGame, GameActivity> _activeGames =
        new(ReferenceEqualityComparer.Instance);

    public IReadOnlyCollection<GameActivity> ActiveGames =>
        new ReadOnlyCollection<GameActivity>(_activeGames.Values.ToList());

    public IReadOnlyList<GameActivityChange> Update(
        IReadOnlyList<IGame> games,
        IReadOnlyList<GameProcessSnapshot> processes)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(processes);

        var changes = new List<GameActivityChange>();
        var detected = new Dictionary<IGame, GameActivity>(ReferenceEqualityComparer.Instance);
        foreach (var game in games)
        {
            var process = processes.FirstOrDefault(candidate =>
                ProtonGameProcessMatcher.Matches(game, candidate));
            if (process == null)
            {
                continue;
            }

            var activity = new GameActivity(game, process.ProcessId, process.ProcessName);
            detected[game] = activity;
            if (!_activeGames.ContainsKey(game))
            {
                changes.Add(new GameActivityChange(GameActivityChangeKind.Started, activity));
            }
        }

        foreach (var previous in _activeGames)
        {
            if (!detected.ContainsKey(previous.Key))
            {
                changes.Add(new GameActivityChange(GameActivityChangeKind.Stopped, previous.Value));
            }
        }

        _activeGames.Clear();
        foreach (var activity in detected)
        {
            _activeGames[activity.Key] = activity.Value;
        }

        return changes;
    }
}

public sealed class PollingGameActivityMonitor : IGameActivityMonitor
{
    private readonly IGameProcessSnapshotProvider _snapshots;
    private readonly TimeSpan _interval;
    private GameActivityDetector _detector = new();
    private readonly object _sync = new();
    private readonly NLog.Logger _logger;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private IReadOnlyList<IGame> _games = [];

    public PollingGameActivityMonitor(
        IGameProcessSnapshotProvider snapshots,
        TimeSpan interval,
        NLog.Logger? logger = null)
    {
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _interval = interval > TimeSpan.Zero
            ? interval
            : throw new ArgumentOutOfRangeException(nameof(interval));
        _logger = logger ?? NLog.LogManager.GetCurrentClassLogger();
    }

    public event Action<GameActivity>? GameStarted;
    public event Action<GameActivity>? GameStopped;

    public IReadOnlyCollection<GameActivity> ActiveGames
    {
        get
        {
            lock (_sync)
            {
                return _detector.ActiveGames;
            }
        }
    }

    public void Start(IReadOnlyList<IGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        lock (_sync)
        {
            if (_worker != null)
            {
                throw new InvalidOperationException("The game activity monitor is already running.");
            }

            _games = games.ToList();
            _detector = new GameActivityDetector();
            _cancellation = new CancellationTokenSource();
            _worker = RunAsync(_cancellation.Token);
        }
    }

    public async Task StopAsync()
    {
        Task? worker;
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            worker = _worker;
            cancellation = _cancellation;
            _worker = null;
            _cancellation = null;
        }

        cancellation?.Cancel();
        if (worker != null)
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Refresh();
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        IReadOnlyList<GameActivityChange> changes;
        try
        {
            var processes = _snapshots.Capture();
            lock (_sync)
            {
                changes = _detector.Update(_games, processes);
            }
        }
        catch (Exception exception)
        {
            _logger.Warn(exception, "Failed to inspect running games");
            return;
        }

        foreach (var change in changes)
        {
            InvokeHandlers(
                change.Kind == GameActivityChangeKind.Started ? GameStarted : GameStopped,
                change.Activity);
        }
    }

    private void InvokeHandlers(Action<GameActivity>? handlers, GameActivity activity)
    {
        if (handlers == null)
        {
            return;
        }

        foreach (Action<GameActivity> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(activity);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Game activity handler failed for {0}", activity.Game.Name);
            }
        }
    }
}

public sealed class SystemGameProcessSnapshotProvider : IGameProcessSnapshotProvider
{
    public IReadOnlyList<GameProcessSnapshot> Capture()
    {
        var result = new List<GameProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var arguments = new List<string>();
                    try
                    {
                        var executablePath = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(executablePath))
                        {
                            arguments.Add(executablePath);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                    catch (NotSupportedException)
                    {
                    }

                    result.Add(new GameProcessSnapshot(
                        process.Id,
                        process.ProcessName,
                        arguments,
                        new Dictionary<string, string>()));
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
        }

        return result;
    }
}
