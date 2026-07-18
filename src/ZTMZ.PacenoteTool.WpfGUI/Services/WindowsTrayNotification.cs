using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Wpf.Ui.Tray;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

internal static class WindowsTrayNotification
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private const uint NimModify = 0x00000001;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;
    private const uint NiifWarning = 0x00000002;
    private const uint NiifError = 0x00000003;
    private const uint NiifNoSound = 0x00000010;
    private const uint NiifRespectQuietTime = 0x00000080;

    // WPF UI does not expose the hidden owner HWND used by its registered tray icon.
    private static readonly FieldInfo? InternalNotifyIconManagerField = typeof(NotifyIconService).GetField(
        "internalNotifyIconManager",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool TryShow(NotifyIconService trayIconService, SystemNotificationMessage notification)
    {
        try
        {
            if (!trayIconService.IsRegistered || trayIconService.Id == 0)
            {
                return false;
            }

            var windowHandle = GetHookWindowHandle(trayIconService);
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            var iconData = new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                hWnd = windowHandle,
                uID = unchecked((uint)trayIconService.Id),
                uFlags = NifInfo,
                szTip = string.Empty,
                szInfo = TrimForShell(notification.Message, 255),
                uTimeoutOrVersion = 10_000,
                szInfoTitle = TrimForShell(notification.Title, 63),
                dwInfoFlags = GetInfoFlags(notification.Severity)
            };

            return ShellNotifyIconW(NimModify, ref iconData);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to display a Windows tray notification.");
            return false;
        }
    }

    private static IntPtr GetHookWindowHandle(NotifyIconService trayIconService)
    {
        try
        {
            var manager = InternalNotifyIconManagerField?.GetValue(trayIconService);
            var hookWindow = manager?.GetType()
                .GetProperty("HookWindow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(manager) as HwndSource;

            return hookWindow?.Handle ?? IntPtr.Zero;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

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

    private static string TrimForShell(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
        {
            return value;
        }

        return value[..Math.Max(0, maximumLength - 3)] + "...";
    }

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
