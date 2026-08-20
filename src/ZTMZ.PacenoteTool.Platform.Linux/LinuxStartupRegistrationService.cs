using System.Text;

namespace ZTMZ.PacenoteTool.Platform.Linux;

/// <summary>
/// Registers the current installation through the freedesktop.org XDG Autostart convention.
/// </summary>
public static class LinuxStartupRegistrationService
{
    private const string DesktopFileName = "com.ztmz.PacenoteTool.desktop";

    public static void SetEnabled(bool enabled, string applicationPath)
    {
        var desktopFilePath = GetDesktopFilePath();
        if (!enabled)
        {
            File.Delete(desktopFilePath);
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);
        var executablePath = Path.GetFullPath(applicationPath);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The application executable for system startup was not found.", executablePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(desktopFilePath)!);
        File.WriteAllText(desktopFilePath, BuildDesktopEntry(executablePath), new UTF8Encoding(false));
    }

    public static string GetCurrentApplicationPath()
    {
        // AppImage runs the managed executable from a temporary mount. APPIMAGE is the stable
        // launcher path that must be used again after the next login.
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath) && File.Exists(appImagePath))
        {
            return appImagePath;
        }

        return Environment.ProcessPath
            ?? throw new InvalidOperationException("Application path is unavailable.");
    }

    internal static string GetDesktopFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            configHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return Path.Combine(configHome, "autostart", DesktopFileName);
    }

    private static string BuildDesktopEntry(string executablePath) =>
        "[Desktop Entry]\n" +
        "Type=Application\n" +
        "Name=ZTMZ Pacenote Tool\n" +
        "Comment=Pacenote playback and rally telemetry dashboards\n" +
        $"Exec={EscapeExecArgument(executablePath)}\n" +
        "Icon=com.ztmz.PacenoteTool\n" +
        "Terminal=false\n" +
        "Categories=Game;\n" +
        "X-GNOME-Autostart-enabled=true\n";

    private static string EscapeExecArgument(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$")}\"";
}
