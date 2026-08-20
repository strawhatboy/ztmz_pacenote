#nullable enable

using System.Diagnostics;
using System.Reflection;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal sealed record AvaloniaDataFolderMigrationRequest(
    string SourcePath,
    string DestinationPath,
    int ParentProcessId,
    string Language)
{
    internal const string CommandLineArgument = "--move-data-folder";
}

internal static class AvaloniaDataFolderMigrationLauncher
{
    public static bool TryParseRequest(string[] arguments, out AvaloniaDataFolderMigrationRequest? request)
    {
        request = null;
        var argumentIndex = Array.FindIndex(arguments, argument =>
            string.Equals(argument, AvaloniaDataFolderMigrationRequest.CommandLineArgument,
                StringComparison.OrdinalIgnoreCase));
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

        request = new AvaloniaDataFolderMigrationRequest(
            sourcePath, destinationPath, parentProcessId, language);
        return true;
    }

    public static void Start(AvaloniaDataFolderMigrationRequest request)
    {
        var startInfo = new ProcessStartInfo(GetExecutablePath()) { UseShellExecute = false };
        startInfo.ArgumentList.Add(AvaloniaDataFolderMigrationRequest.CommandLineArgument);
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
        Process.Start(new ProcessStartInfo(GetExecutablePath()) { UseShellExecute = true });
    }

    private static string GetExecutablePath()
    {
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImage) && File.Exists(appImage))
        {
            return appImage;
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath))
        {
            return assemblyPath;
        }

        throw new FileNotFoundException("The application executable could not be found.");
    }
}
