using System.Reflection;
using System.Runtime.InteropServices;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Platform.Windows;

/// <summary>
/// Associates a shell notification balloon with Avalonia's already-registered tray icon.
/// </summary>
public sealed class WindowsSystemNotificationService : ISystemNotificationService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private const uint NimModify = 0x00000001;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;
    private const uint NiifWarning = 0x00000002;
    private const uint NiifError = 0x00000003;
    private const uint NiifNoSound = 0x00000010;
    private const uint NiifRespectQuietTime = 0x00000080;
    private readonly Func<object?> _trayIconImplementationProvider;

    public WindowsSystemNotificationService(Func<object?> trayIconImplementationProvider)
    {
        _trayIconImplementationProvider = trayIconImplementationProvider;
    }

    public Task<bool> TryShowAsync(
        SystemNotification notification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var trayIconImplementation = _trayIconImplementationProvider();
            if (trayIconImplementation == null ||
                !TryGetTrayIconId(trayIconImplementation, out var trayIconId))
            {
                Logger.Debug("Avalonia's Windows tray icon is not ready for a system notification.");
                return Task.FromResult(false);
            }

            if (!TryGetTrayHostWindow(trayIconImplementation, out var trayHostWindow))
            {
                Logger.Debug("Avalonia's Windows tray host is not ready for a system notification.");
                return Task.FromResult(false);
            }

            var iconData = CreateNotificationData(trayHostWindow, trayIconId, notification);
            return Task.FromResult(ShellNotifyIconW(NimModify, ref iconData));
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to display a Windows system notification.");
            return Task.FromResult(false);
        }
    }

    private static bool TryGetTrayIconId(object trayIconImplementation, out uint trayIconId)
    {
        var uniqueIdField = trayIconImplementation.GetType().GetField(
            "_uniqueId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (uniqueIdField?.GetValue(trayIconImplementation) is int uniqueId && uniqueId > 0)
        {
            trayIconId = unchecked((uint)uniqueId);
            return true;
        }

        trayIconId = 0;
        return false;
    }

    private static bool TryGetTrayHostWindow(object trayIconImplementation, out IntPtr windowHandle)
    {
        // Avalonia.Win32 registers every TrayIcon against Win32Platform.Instance.Handle.
        var platformType = trayIconImplementation.GetType().Assembly.GetType("Avalonia.Win32.Win32Platform");
        var platformInstance = platformType?.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(null);
        if (platformInstance != null &&
            platformType?.GetProperty(
                    "Handle",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(platformInstance) is IntPtr handle &&
            handle != IntPtr.Zero)
        {
            windowHandle = handle;
            return true;
        }

        windowHandle = IntPtr.Zero;
        return false;
    }

    private static NotifyIconData CreateNotificationData(
        IntPtr trayHostWindow,
        uint trayIconId,
        SystemNotification notification) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = trayHostWindow,
        uID = trayIconId,
        uFlags = NifInfo,
        szTip = string.Empty,
        szInfo = TrimForShell(notification.Message, 255),
        uTimeoutOrVersion = 10_000,
        szInfoTitle = TrimForShell(notification.Title, 63),
        dwInfoFlags = GetInfoFlags(notification.Severity)
    };

    private static uint GetInfoFlags(SystemNotificationSeverity severity)
    {
        var iconFlag = severity switch
        {
            SystemNotificationSeverity.Warning => NiifWarning,
            SystemNotificationSeverity.Error => NiifError,
            _ => NiifInfo
        };

        return iconFlag | NiifNoSound | NiifRespectQuietTime;
    }

    private static string TrimForShell(string value, int maximumLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maximumLength
            ? value
            : value[..Math.Max(0, maximumLength - 3)] + "...";

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint message, ref NotifyIconData iconData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}
