#nullable enable

using System;
using System.IO;

namespace ZTMZ.PacenoteTool.Base;

public interface IConfigurationLocation
{
    string? GetConfiguredDataRoot();

    void SetConfiguredDataRoot(string path);
}

public interface IApplicationPaths
{
    string DataRoot { get; }

    string GetPath(string path);

    bool IsPortable { get; }

    void SetDataRootForNextLaunch(string folderPath);
}

public sealed class EnvironmentConfigurationLocation : IConfigurationLocation
{
    public const string DataHomeEnvironmentVariable = "ZTMZ_DATA_HOME";

    public string? GetConfiguredDataRoot()
    {
        return Environment.GetEnvironmentVariable(DataHomeEnvironmentVariable);
    }

    public void SetConfiguredDataRoot(string path)
    {
        throw new PlatformNotSupportedException(
            $"This host cannot persist a custom data folder. Set {DataHomeEnvironmentVariable} instead.");
    }
}

public static class ApplicationDataPathDefaults
{
    public static string ResolveCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return ResolveWindows(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        }

        if (OperatingSystem.IsLinux())
        {
            return ResolveLinux(
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(applicationData))
        {
            applicationData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(applicationData, "ZTMZ");
    }

    public static string ResolveWindows(string documentsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsDirectory);
        return Path.Combine(documentsDirectory, "My Games", "ZTMZClub_nextgen");
    }

    public static string ResolveLinux(string? xdgDataHome, string userProfileDirectory)
    {
        var dataHome = string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(
                RequireUserProfile(userProfileDirectory),
                ".local",
                "share")
            : xdgDataHome;

        return Path.Combine(dataHome, "ztmz");
    }

    private static string RequireUserProfile(string userProfileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileDirectory);
        return userProfileDirectory;
    }
}
