#nullable enable

using System;
using System.Text.RegularExpressions;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

internal static class RBRModUpdateDetection
{
    public static bool HasManagedUpdate(string? installedVersion, string? installedHash, GameModDefinition mod)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
            return true;
        int comparison = CompareVersions(installedVersion, mod.Version);
        return comparison < 0 || (comparison == 0 &&
            !string.Equals(installedHash, mod.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    public static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion))
            return leftVersion.CompareTo(rightVersion);

        // Include every numeric component, including package dates and bundled audio revisions.
        var leftParts = Regex.Matches(left, @"[0-9]+|[^0-9]+");
        var rightParts = Regex.Matches(right, @"[0-9]+|[^0-9]+");
        for (int index = 0; index < Math.Min(leftParts.Count, rightParts.Count); index++)
        {
            string a = leftParts[index].Value, b = rightParts[index].Value;
            int comparison;
            if (char.IsAsciiDigit(a[0]) && char.IsAsciiDigit(b[0]))
            {
                a = a.TrimStart('0');
                b = b.TrimStart('0');
                comparison = a.Length.CompareTo(b.Length);
                if (comparison == 0)
                    comparison = StringComparer.Ordinal.Compare(a, b);
            }
            else
                comparison = StringComparer.OrdinalIgnoreCase.Compare(a, b);
            if (comparison != 0)
                return comparison;
        }
        return leftParts.Count.CompareTo(rightParts.Count);
    }

    public static bool IsOlderExternalFolder(GameModDefinition mod, string installedFolder)
    {
        var latestFolder = string.IsNullOrWhiteSpace(mod.ExternalFolderName)
            ? mod.PackageRootDirectory : mod.ExternalFolderName;
        // JSGME has no version manifest. Only compare folders following the catalog's naming
        // convention; an arbitrary renamed folder is not evidence of an older version.
        return !string.IsNullOrWhiteSpace(mod.ExternalFolderPrefix) &&
            installedFolder.StartsWith(mod.ExternalFolderPrefix, StringComparison.OrdinalIgnoreCase) &&
            latestFolder.StartsWith(mod.ExternalFolderPrefix, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Regex.Replace(installedFolder, "[0-9]+", "#"),
                Regex.Replace(latestFolder, "[0-9]+", "#"), StringComparison.OrdinalIgnoreCase) &&
            CompareVersions(installedFolder, latestFolder) < 0;
    }
}
