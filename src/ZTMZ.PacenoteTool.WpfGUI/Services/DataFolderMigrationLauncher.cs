using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

internal sealed record DataFolderMigrationRequest(
    string SourcePath,
    string DestinationPath,
    int ParentProcessId,
    string Language)
{
    internal const string CommandLineArgument = "--move-data-folder";
}

internal static class DataFolderMigrationLauncher
{
    public static bool TryParseRequest(string[] arguments, out DataFolderMigrationRequest? request)
    {
        request = null;
        var argumentIndex = Array.FindIndex(arguments, argument =>
            string.Equals(argument, DataFolderMigrationRequest.CommandLineArgument, StringComparison.OrdinalIgnoreCase));
        if (argumentIndex < 0 || arguments.Length < argumentIndex + 5)
        {
            return false;
        }

        var sourcePath = arguments[argumentIndex + 1];
        var destinationPath = arguments[argumentIndex + 2];
        var parentProcessIdText = arguments[argumentIndex + 3];
        var language = arguments[argumentIndex + 4];
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            string.IsNullOrWhiteSpace(destinationPath) ||
            !int.TryParse(parentProcessIdText, out var parentProcessId) ||
            parentProcessId <= 0)
        {
            return false;
        }

        request = new DataFolderMigrationRequest(sourcePath, destinationPath, parentProcessId, language);
        return true;
    }

    public static void Start(DataFolderMigrationRequest request)
    {
        var startInfo = new ProcessStartInfo(GetExecutablePath())
        {
            UseShellExecute = false
        };
        startInfo.Environment.Remove("ZTMZ_STARTUP_STATUS");
        startInfo.ArgumentList.Add(DataFolderMigrationRequest.CommandLineArgument);
        startInfo.ArgumentList.Add(request.SourcePath);
        startInfo.ArgumentList.Add(request.DestinationPath);
        startInfo.ArgumentList.Add(request.ParentProcessId.ToString());
        startInfo.ArgumentList.Add(request.Language);

        if (Process.Start(startInfo) == null)
        {
            throw new InvalidOperationException("The data-folder migration helper could not be started.");
        }
    }

    public static void RestartApplication()
    {
        Process.Start(new ProcessStartInfo(GetNormalLaunchPath())
        {
            UseShellExecute = true
        });
    }

    internal static string GetNormalLaunchPath()
    {
        var launcher = Path.Combine(AppContext.BaseDirectory, "ZTMZ.Launcher.exe");
        return File.Exists(launcher) ? launcher : GetExecutablePath();
    }

    private static string GetExecutablePath()
    {
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            var executablePath = Path.ChangeExtension(entryAssemblyPath, ".exe");
            if (File.Exists(executablePath))
            {
                return executablePath;
            }
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        throw new FileNotFoundException("The application executable could not be found.");
    }
}
