#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

/// <summary>One opt-in prompt per game session, independent of the desktop UI.</summary>
public sealed class LocalBridgePromptCoordinator : IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<IGame, object> sessions = new();
    private readonly Func<ILocalGameBridgeControl, string?, Func<bool>, Task<bool>> confirm;
    private readonly Func<ILocalGameBridgeControl, string, Task> failure;
    private bool disposed;

    public LocalBridgePromptCoordinator(
        Func<ILocalGameBridgeControl, string?, Func<bool>, Task<bool>> confirm,
        Func<ILocalGameBridgeControl, string, Task> failure)
    {
        this.confirm = confirm;
        this.failure = failure;
        GameRuntimeNotifications.LocalBridgeControlsEnabled += OnEnabled;
    }

    private void OnEnabled(IGame game) { Reset(game); Request(game); }
    public void Reset(IGame game) { lock (sync) sessions.Remove(game); }
    public void Request(IGame game) => _ = RequestAsync(game);

    public async Task RequestAsync(IGame game)
    {
        try
        {
            if (game is not ILocalGameBridgeControl bridge || !Eligible(game, bridge)) return;
            var session = new object();
            lock (sync)
            {
                if (disposed || sessions.ContainsKey(game)) return;
                sessions.Add(game, session);
            }
            bool Current()
            {
                lock (sync)
                    return !disposed && sessions.TryGetValue(game, out var current) && ReferenceEquals(current, session)
                        && Eligible(game, bridge);
            }
            using var lease = await InjectionConfirmationQueue.EnterAsync();
            if (!Current() || !await confirm(bridge, null, Current) || !Current()) return;
            var result = await bridge.InjectLocalBridgeAsync();
            if (result.ConfirmAndRetry is { } retry)
            {
                if (!Current() || !await confirm(bridge, result.Detail, Current) || !Current()) return;
                result = await retry();
            }
            if (result.Succeeded) GameRuntimeNotifications.NotifyLocalBridgeInjected(game);
            else if (Current()) await failure(bridge, result.Detail);
        }
        catch (Exception error)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(error, "Local bridge prompt failed for {0}", game.Name);
        }
    }

    private static bool Eligible(IGame game, ILocalGameBridgeControl bridge)
    {
        var status = bridge.GetLocalBridgeDiagnostics();
        return game.IsRunning && status.IsEnabled && status.IsAvailable && !status.IsInjected;
    }

    public void Dispose()
    {
        lock (sync) { disposed = true; sessions.Clear(); }
        GameRuntimeNotifications.LocalBridgeControlsEnabled -= OnEnabled;
    }
}
