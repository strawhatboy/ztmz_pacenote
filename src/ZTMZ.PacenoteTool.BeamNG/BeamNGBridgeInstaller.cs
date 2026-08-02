using System;
using System.IO;
using System.IO.Compression;

namespace ZTMZ.PacenoteTool.BeamNG;

public static class BeamNGBridgeInstaller
{
    public const string ModFileName = "ztmz_pacenote_rally_bridge.zip";
    private const string BundledModDirectoryName = "BeamNGMod";

    public static bool IsInstalled(BeamNGRallyBridgeConfig config)
    {
        if (!BeamNGUserFolderLocator.IsValidUserFolder(config.UserFolderPath))
        {
            return false;
        }

        var modsDirectory = Path.Combine(config.UserFolderPath, "mods");
        return File.Exists(Path.Combine(modsDirectory, ModFileName)) ||
               Directory.Exists(Path.Combine(modsDirectory, "unpacked", "ztmz_pacenote_rally_bridge"));
    }

    public static string Install(BeamNGRallyBridgeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!BeamNGUserFolderLocator.IsValidUserFolder(config.UserFolderPath))
        {
            throw new DirectoryNotFoundException("Select the BeamNG.drive user folder before installing the Rally bridge.");
        }

        var sourceDirectory = GetBundledModDirectory();
        if (!File.Exists(Path.Combine(sourceDirectory, "mod_info.json")))
        {
            throw new FileNotFoundException("The bundled BeamNG Rally bridge files are missing.", sourceDirectory);
        }

        var modsDirectory = Path.Combine(Path.GetFullPath(config.UserFolderPath), "mods");
        Directory.CreateDirectory(modsDirectory);

        var targetPath = Path.Combine(modsDirectory, ModFileName);
        var temporaryPath = Path.Combine(modsDirectory, $".{ModFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            ZipFile.CreateFromDirectory(sourceDirectory, temporaryPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return targetPath;
    }

    private static string GetBundledModDirectory()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(BeamNGBridgeInstaller).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            throw new InvalidOperationException("Cannot locate the BeamNG game provider directory.");
        }

        return Path.Combine(assemblyDirectory, BundledModDirectoryName);
    }
}
