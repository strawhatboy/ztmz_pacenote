using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.Base;

public enum SystemNotificationSeverity
{
    Information,
    Warning,
    Error
}

public sealed record SystemNotification(
    string Title,
    string Message,
    SystemNotificationSeverity Severity = SystemNotificationSeverity.Information);

/// <summary>
/// Sends notifications through the host operating system rather than an application window.
/// </summary>
public interface ISystemNotificationService
{
    Task<bool> TryShowAsync(
        SystemNotification notification,
        CancellationToken cancellationToken = default);
}
