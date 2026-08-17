#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace ZTMZ.PacenoteTool.Base;

public class AppLevelVariables : IApplicationPaths
{
    private static readonly object InstanceLock = new();
    private static IConfigurationLocation _configurationLocation = new EnvironmentConfigurationLocation();
    private static AppLevelVariables? _instance;

    private readonly IConfigurationLocation _location;
    private readonly object _pathLock = new();
    private string? _appConfigFolder;

    public AppLevelVariables()
        : this(new EnvironmentConfigurationLocation())
    {
    }

    public AppLevelVariables(IConfigurationLocation location)
    {
        _location = location ?? throw new ArgumentNullException(nameof(location));
    }

    public string AppConfigFolder
    {
        get
        {
            if (_appConfigFolder != null)
            {
                return _appConfigFolder;
            }

            lock (_pathLock)
            {
                if (_appConfigFolder != null)
                {
                    return _appConfigFolder;
                }

                var configuredPath = _location.GetConfiguredDataRoot();
                _appConfigFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                    string.IsNullOrWhiteSpace(configuredPath)
                        ? ApplicationDataPathDefaults.ResolveCurrentPlatform()
                        : configuredPath));
                Directory.CreateDirectory(_appConfigFolder);
                return _appConfigFolder;
            }
        }
    }

    public string DataRoot => AppConfigFolder;

    public static AppLevelVariables Instance
    {
        get
        {
            lock (InstanceLock)
            {
                return _instance ??= new AppLevelVariables(_configurationLocation);
            }
        }
    }

    public static List<char> InvalidCharsForWindowsPath { get; } =
    [
        '*', '{', '}', '!', '"', '?'
    ];

    public bool IsPortable => IsPortableVersion();

    public static void Configure(IConfigurationLocation configurationLocation)
    {
        ArgumentNullException.ThrowIfNull(configurationLocation);

        lock (InstanceLock)
        {
            if (_instance != null)
            {
                throw new InvalidOperationException(
                    "Application paths must be configured before they are first used.");
            }

            _configurationLocation = configurationLocation;
        }
    }

    public string GetPath(string path)
    {
        foreach (var c in InvalidCharsForWindowsPath)
        {
            path = path.Replace(c, '_');
        }

        path = path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
#if RELEASE_PORTABLE
        return path;
#else
        return Path.Join(AppConfigFolder, path);
#endif
    }

    public bool IsPortableVersion()
    {
#if RELEASE_PORTABLE
        return true;
#else
        return false;
#endif
    }

    public void SetAppConfigFolderForNextLaunch(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("A data-folder path is required.", nameof(folderPath));
        }

        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"The data folder does not exist: {normalizedPath}");
        }

        _location.SetConfiguredDataRoot(normalizedPath);
    }

    public void SetDataRootForNextLaunch(string folderPath)
    {
        SetAppConfigFolderForNextLaunch(folderPath);
    }
}
