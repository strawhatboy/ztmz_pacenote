using Microsoft.Win32;

namespace ZTMZ.PacenoteTool.Platform.Windows;

public static class WindowsStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZTMZClub.PacenoteTool";

    public static void SetEnabled(bool enabled, string applicationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        if (enabled)
        {
            key.SetValue(
                ValueName,
                $"\"{Path.GetFullPath(applicationPath)}\"",
                RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
