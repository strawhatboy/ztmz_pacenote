using System;
using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base;

public static class CoDriverSelectionIdentity
{
    private const string IdPrefix = "id:";
    private const string PathPrefix = "path:";

    public static string Create(CoDriverPackageInfo package)
    {
        if (package == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(package.id))
        {
            return IdPrefix + package.id.Trim();
        }

        return string.IsNullOrWhiteSpace(package.Path)
            ? string.Empty
            : PathPrefix + package.Path.Trim();
    }

    public static int ResolveIndex(
        IReadOnlyList<CoDriverPackageInfo> packages,
        string identity,
        int fallbackIndex)
    {
        if (packages == null || packages.Count == 0)
        {
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(identity))
        {
            for (var index = 0; index < packages.Count; index++)
            {
                if (Matches(packages[index], identity))
                {
                    return index;
                }
            }
        }

        return fallbackIndex >= 0 && fallbackIndex < packages.Count
            ? fallbackIndex
            : -1;
    }

    private static bool Matches(CoDriverPackageInfo package, string identity)
    {
        if (string.Equals(Create(package), identity.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Tolerate unprefixed values written by early development builds.
        return !identity.Contains(':') &&
               (string.Equals(package.id, identity, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Path, identity, StringComparison.OrdinalIgnoreCase));
    }
}
