using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace ZTMZ.PacenoteTool.BeamNG;

public enum BeamNGBridgeInstallationState
{
    Missing,
    Current,
    Outdated
}

public sealed record BeamNGBridgeInstallationInfo(
    BeamNGBridgeInstallationState State,
    Version? InstalledVersion,
    Version? BundledVersion)
{
    public bool IsCurrent => State == BeamNGBridgeInstallationState.Current;
}

public static class BeamNGBridgeInstaller
{
    public const string ModFileName = "ztmz_pacenote_rally_bridge.zip";
    public const string ModMetadataId = "ZTMZPACENOTERALLYBRIDGE";
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

    public static BeamNGBridgeInstallationInfo GetInstallationInfo(BeamNGRallyBridgeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!BeamNGUserFolderLocator.IsValidUserFolder(config.UserFolderPath))
        {
            return new BeamNGBridgeInstallationInfo(BeamNGBridgeInstallationState.Missing, null, null);
        }

        var modsDirectory = Path.Combine(Path.GetFullPath(config.UserFolderPath), "mods");
        var archivePath = Path.Combine(modsDirectory, ModFileName);
        var unpackedPath = Path.Combine(modsDirectory, "unpacked", "ztmz_pacenote_rally_bridge");
        var hasArchive = File.Exists(archivePath);
        var hasUnpacked = Directory.Exists(unpackedPath);
        if (!hasArchive && !hasUnpacked)
        {
            return new BeamNGBridgeInstallationInfo(BeamNGBridgeInstallationState.Missing, null, null);
        }

        var bundledVersion = GetBundledModVersion();
        Version? installedVersion = null;
        if (hasArchive)
        {
            installedVersion = TryReadArchiveVersion(archivePath);
        }

        if (hasUnpacked)
        {
            var unpackedVersion = TryReadMetadataFileVersion(
                Path.Combine(unpackedPath, "mod_info", ModMetadataId, "info.json"));
            if (unpackedVersion is not null &&
                (installedVersion is null || unpackedVersion.CompareTo(installedVersion) > 0))
            {
                installedVersion = unpackedVersion;
            }
        }

        var state = installedVersion is not null && installedVersion.CompareTo(bundledVersion) >= 0
            ? BeamNGBridgeInstallationState.Current
            : BeamNGBridgeInstallationState.Outdated;
        return new BeamNGBridgeInstallationInfo(state, installedVersion, bundledVersion);
    }

    public static string Install(BeamNGRallyBridgeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!BeamNGUserFolderLocator.IsValidUserFolder(config.UserFolderPath))
        {
            throw new DirectoryNotFoundException("Select the BeamNG.drive user folder before installing the Rally bridge.");
        }

        var sourceDirectory = GetBundledModDirectory();
        var metadataPath = Path.Combine(sourceDirectory, "mod_info", ModMetadataId, "info.json");
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("The bundled BeamNG Rally bridge files are missing.", sourceDirectory);
        }

        var modsDirectory = Path.Combine(Path.GetFullPath(config.UserFolderPath), "mods");
        Directory.CreateDirectory(modsDirectory);

        var targetPath = Path.Combine(modsDirectory, ModFileName);
        var temporaryPath = Path.Combine(modsDirectory, $".{ModFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            CreateArchive(sourceDirectory, temporaryPath);
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

    private static Version GetBundledModVersion()
    {
        var metadataPath = Path.Combine(GetBundledModDirectory(), "mod_info", ModMetadataId, "info.json");
        var version = TryReadMetadataFileVersion(metadataPath);
        if (version is null)
        {
            throw new InvalidOperationException("The bundled BeamNG Rally bridge version metadata is invalid.");
        }

        return version;
    }

    private static Version? TryReadArchiveVersion(string archivePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = FindEntry(archive, $"mod_info/{ModMetadataId}/info.json");
            if (entry is null)
            {
                return null;
            }

            using var stream = entry.Open();
            return TryReadMetadataVersion(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void CreateArchive(string sourceDirectory, string archivePath)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var entryName = NormalizeArchivePath(Path.GetRelativePath(sourceDirectory, sourcePath));
            archive.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
        }
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string expectedPath)
    {
        var normalizedExpectedPath = NormalizeArchivePath(expectedPath);
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(NormalizeArchivePath(entry.FullName), normalizedExpectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static string NormalizeArchivePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static Version? TryReadMetadataFileVersion(string metadataPath)
    {
        try
        {
            using var stream = File.OpenRead(metadataPath);
            return TryReadMetadataVersion(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Version? TryReadMetadataVersion(Stream stream)
    {
        try
        {
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tagid", out var tagId) ||
                !string.Equals(tagId.GetString(), ModMetadataId, StringComparison.Ordinal) ||
                !root.TryGetProperty("version_string", out var versionString) ||
                !Version.TryParse(versionString.GetString(), out var version))
            {
                return null;
            }

            return version;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
