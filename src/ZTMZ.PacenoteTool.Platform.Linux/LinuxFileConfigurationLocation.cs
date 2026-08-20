#nullable enable

using System.Text;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Platform.Linux;

/// <summary>Persists an optional Linux Data Home outside the folder being moved.</summary>
public sealed class LinuxFileConfigurationLocation : IConfigurationLocation
{
    private const string DataRootFileName = "data-root";

    public string? GetConfiguredDataRoot()
    {
        var environmentPath = Environment.GetEnvironmentVariable(
            EnvironmentConfigurationLocation.DataHomeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return environmentPath;
        }

        var configurationFile = GetConfigurationFilePath();
        return File.Exists(configurationFile)
            ? File.ReadAllText(configurationFile, Encoding.UTF8).Trim()
            : null;
    }

    public void SetConfiguredDataRoot(string path)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                EnvironmentConfigurationLocation.DataHomeEnvironmentVariable)))
        {
            throw new InvalidOperationException(
                $"{EnvironmentConfigurationLocation.DataHomeEnvironmentVariable} is set and must be changed outside the application.");
        }

        var configurationFile = GetConfigurationFilePath();
        var directory = Path.GetDirectoryName(configurationFile)!;
        Directory.CreateDirectory(directory);
        var temporaryFile = configurationFile + ".tmp";
        File.WriteAllText(temporaryFile, path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryFile, configurationFile, overwrite: true);
    }

    private static string GetConfigurationFilePath()
    {
        var configurationHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationHome))
        {
            configurationHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return Path.Combine(configurationHome, "ztmz", DataRootFileName);
    }
}
