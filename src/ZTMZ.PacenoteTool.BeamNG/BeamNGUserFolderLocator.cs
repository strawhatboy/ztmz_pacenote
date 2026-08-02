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
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    public static bool IsValidUserFolder(string? userFolderPath)
    {
        return !string.IsNullOrWhiteSpace(userFolderPath) && Directory.Exists(userFolderPath);
    }

    private static IEnumerable<string> GetCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var beamNgLocalRoot = Path.Combine(localAppData, "BeamNG.drive");

        yield return Path.Combine(beamNgLocalRoot, "current");
        yield return Path.Combine(documents, "BeamNG.drive");

        if (!Directory.Exists(beamNgLocalRoot))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(beamNgLocalRoot)
                     .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (Version.TryParse(Path.GetFileName(directory), out _))
            {
                yield return directory;
            }
        }
    }
}
