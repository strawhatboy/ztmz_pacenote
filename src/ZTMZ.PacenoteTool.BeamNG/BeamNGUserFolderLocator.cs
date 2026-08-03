using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZTMZ.PacenoteTool.BeamNG;

public static class BeamNGUserFolderLocator
{
    public static string FindDefaultUserFolder()
    {
        foreach (var candidate in GetCandidates())
        {
            if (IsValidUserFolder(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    public static bool IsValidUserFolder(string? userFolderPath)
    {
        return !string.IsNullOrWhiteSpace(userFolderPath) &&
               Directory.Exists(userFolderPath) &&
               !File.Exists(Path.Combine(userFolderPath, "moved here.lnk"));
    }

    private static IEnumerable<string> GetCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var beamNgRoot = Path.Combine(localAppData, "BeamNG");
        var beamNgUserRoot = Path.Combine(beamNgRoot, "BeamNG.drive");
        var configuredUserFolder = ReadConfiguredUserFolder(Path.Combine(beamNgRoot, "BeamNG.drive.ini"));

        if (!string.IsNullOrWhiteSpace(configuredUserFolder))
        {
            yield return configuredUserFolder;
        }

        yield return Path.Combine(beamNgUserRoot, "current");

        foreach (var directory in GetVersionDirectories(beamNgUserRoot))
        {
            yield return directory;
        }

        // BeamNG used this layout before moving the user folder beneath the BeamNG root.
        var legacyBeamNgRoot = Path.Combine(localAppData, "BeamNG.drive");
        yield return Path.Combine(legacyBeamNgRoot, "current");

        foreach (var directory in GetVersionDirectories(legacyBeamNgRoot))
        {
            yield return directory;
        }

        // Keep legacy Documents installs as a final fallback. A moved here.lnk marker
        // identifies the retired redirect folder and IsValidUserFolder rejects it.
        yield return Path.Combine(documents, "BeamNG.drive");
    }

    private static IEnumerable<string> GetVersionDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(root)
                     .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (Version.TryParse(Path.GetFileName(directory), out _))
            {
                yield return directory;
            }
        }
    }

    private static string ReadConfiguredUserFolder(string iniPath)
    {
        if (!File.Exists(iniPath))
        {
            return string.Empty;
        }

        try
        {
            foreach (var line in File.ReadLines(iniPath))
            {
                var separator = line.IndexOf('=');
                if (separator < 0 ||
                    !string.Equals(line[..separator].Trim(), "userFolder", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var configuredPath = Environment.ExpandEnvironmentVariables(line[(separator + 1)..].Trim().Trim('"'));
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    return string.Empty;
                }

                if (Path.IsPathFullyQualified(configuredPath))
                {
                    return Path.GetFullPath(configuredPath);
                }

                var iniDirectory = Path.GetDirectoryName(iniPath);
                return string.IsNullOrWhiteSpace(iniDirectory)
                    ? string.Empty
                    : Path.GetFullPath(Path.Combine(iniDirectory, configuredPath));
            }
        }
        catch (IOException)
        {
            // Fall through to the standard user-folder location.
        }
        catch (UnauthorizedAccessException)
        {
            // Fall through to the standard user-folder location.
        }

        return string.Empty;
    }
}
