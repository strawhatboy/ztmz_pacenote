using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

internal enum SystemNotificationSeverity
{
    Information,
    Warning,
    Error
}

internal sealed record SystemNotificationMessage(
    string Title,
    string Message,
    SystemNotificationSeverity Severity = SystemNotificationSeverity.Information);

public sealed class SystemNotificationService : IHostedService, IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ZTMZPacenoteTool _tool;
    private int _isStarted;

    internal event Action<SystemNotificationMessage>? NotificationRequested;

    public SystemNotificationService(ZTMZPacenoteTool tool)
    {
        _tool = tool;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isStarted, 1) != 0)
        {
            return Task.CompletedTask;
        }

        _tool.onGameStarted += OnGameStarted;
        _tool.onGameEnded += OnGameEnded;
        _tool.onRaceBegin += OnRaceBegin;
        _tool.onRaceEnd += OnRaceEnd;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void ShowTestNotification()
    {
        Publish("notification.test");
    }

    public void ShowVoicePackageUpdates(int count) => Publish("voiceStartup.available", count);

    private void OnGameStarted(IGame game, Process process)
    {
        Publish("notification.gameStarted", game.Name);
    }

    private void OnGameEnded(IGame game)
    {
        Publish("notification.gameEnded", game.Name);
    }

    private void OnRaceBegin(IGame game)
    {
        Publish("notification.stageStarted", GetStageName(game));
    }

    private void OnRaceEnd(IGame game)
    {
        Publish("notification.stageFinished", GetStageName(game));
    }

    private static string GetStageName(IGame game)
    {
        var trackName = game.GameDataReader.TrackName;
        return string.IsNullOrWhiteSpace(trackName) ? game.Name : trackName;
    }

    private void Publish(string messageKey, params object[] arguments)
    {
        if (!Config.Instance.EnableWindowsNotifications)
        {
            return;
        }

        try
        {
            var message = string.Format(I18NLoader.Instance[messageKey], arguments);
            NotificationRequested?.Invoke(new SystemNotificationMessage(
                I18NLoader.Instance["notification.title"],
                message));
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to publish a Windows notification.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isStarted, 0) == 0)
        {
            return;
        }

        _tool.onGameStarted -= OnGameStarted;
        _tool.onGameEnded -= OnGameEnded;
        _tool.onRaceBegin -= OnRaceBegin;
        _tool.onRaceEnd -= OnRaceEnd;
    }
}
