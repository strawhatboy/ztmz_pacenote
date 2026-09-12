using System;
using System.IO;

namespace ZTMZ.PacenoteTool.Core;

internal static class MispackagedAcrProfileMigration
{
    // Only the accidentally distributed default ACR profiles are in scope.
    internal static void Remove(string dataHome)
    {
        var root = Path.GetFullPath(string.IsNullOrEmpty(dataHome) ? "." : dataHome);
        var target = Path.Combine(root, "profiles", "default", "Assetto Corsa Rally");
        var current = root;
        foreach (var part in new[] { "profiles", "default", "Assetto Corsa Rally" })
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current)) return;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to follow a linked profile directory: " + current);
        }
        Directory.Delete(target, recursive: true);
    }
}
