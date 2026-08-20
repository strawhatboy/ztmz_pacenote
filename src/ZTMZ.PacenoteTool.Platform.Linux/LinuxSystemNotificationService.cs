using Tmds.DBus;
using ZTMZ.PacenoteTool.Base;
using System.Threading;

namespace ZTMZ.PacenoteTool.Platform.Linux;

/// <summary>
/// Uses the desktop environment's standard freedesktop.org notification service.
/// </summary>
public sealed class LinuxSystemNotificationService : ISystemNotificationService, IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private const string ServiceName = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";
    private readonly Connection _connection = new(Address.Session);
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IDesktopNotifications? _notifications;
    private bool _disposed;

    public async Task<bool> TryShowAsync(
        SystemNotification notification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var notifications = await GetNotificationsAsync(cancellationToken).ConfigureAwait(false);
            await notifications.NotifyAsync(
                    "ZTMZ Pacenote Tool",
                    0,
                    "com.ztmz.PacenoteTool",
                    notification.Title,
                    notification.Message,
                    [],
                    new Dictionary<string, object>
                    {
                        ["desktop-entry"] = "com.ztmz.PacenoteTool",
                        ["urgency"] = GetUrgency(notification.Severity)
                    },
                    -1)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            Logger.Debug("Displayed Linux desktop notification: {0}", notification.Title);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Error(exception, "Failed to display a Linux desktop notification.");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
        _connectionGate.Dispose();
    }

    private async Task<IDesktopNotifications> GetNotificationsAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notifications != null)
        {
            return _notifications;
        }

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_notifications == null)
            {
                await _connection.ConnectAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                _notifications = _connection.CreateProxy<IDesktopNotifications>(
                    ServiceName,
                    new ObjectPath(ObjectPath));
            }

            return _notifications;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private static byte GetUrgency(SystemNotificationSeverity severity) => severity switch
    {
        SystemNotificationSeverity.Warning => 1,
        SystemNotificationSeverity.Error => 2,
        _ => 0
    };

    [DBusInterface(ServiceName)]
    // Tmds.DBus emits the proxy into a separate dynamic assembly, so the
    // contract must be public for that generated type to implement it.
    public interface IDesktopNotifications : IDBusObject
    {
        Task<uint> NotifyAsync(
            string applicationName,
            uint replacesId,
            string applicationIcon,
            string summary,
            string body,
            string[] actions,
            IDictionary<string, object> hints,
            int expireTimeout);
    }
}
