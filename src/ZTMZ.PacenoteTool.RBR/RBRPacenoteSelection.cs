using System;
using System.IO;
using System.Linq;
using IniParser;

namespace ZTMZ.PacenoteTool.RBR;

internal enum RBRPacenoteSelectionStatus
{
    NotConfigured, Disabled, DirectoryMissing, StageMissing, DefaultSelected, CustomSelected, Unknown
}

// This describes NGPCarMenu's on-disk selection, not proof of what PaceNote.dll loaded.
internal sealed record RBRPacenoteSelection(RBRPacenoteSelectionStatus Status, string FilePath = "")
{
    public bool UseCustomDefinition => FilePath.Length > 0 &&
        !Path.GetFileNameWithoutExtension(FilePath).EndsWith("_default", StringComparison.OrdinalIgnoreCase);

    public string MessageKey => "game.rbr.message.selection_" + Status.ToString();
    public int MessageLevel => Status is RBRPacenoteSelectionStatus.Unknown or RBRPacenoteSelectionStatus.DirectoryMissing ? 2 : 0;
}

internal static class RBRPacenoteSelectionResolver
{
    public static RBRPacenoteSelection Resolve(string root, string trackName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root))
                return new(RBRPacenoteSelectionStatus.NotConfigured);
            var config = Path.Combine(root, "Plugins", "NGPCarMenu.ini");
            if (!File.Exists(config))
                return new(RBRPacenoteSelectionStatus.NotConfigured);
            var ini = new FileIniDataParser().ReadFile(config);
            var section = ini.Sections.FirstOrDefault(s => s.SectionName.Equals("Default", StringComparison.OrdinalIgnoreCase));
            var path = section?.Keys["MyPacenotesPath"]?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
                return new(RBRPacenoteSelectionStatus.NotConfigured);
            if (path.Equals("disabled", StringComparison.OrdinalIgnoreCase))
                return new(RBRPacenoteSelectionStatus.Disabled);
            return ResolveDirectory(Path.GetFullPath(path, root), trackName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or IniParser.Exceptions.ParsingException)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Unable to resolve RBR pacenote selection");
            return new(RBRPacenoteSelectionStatus.Unknown);
        }
    }

    internal static RBRPacenoteSelection ResolveDirectory(string directory, string trackName)
    {
        if (!Directory.Exists(directory))
            return new(RBRPacenoteSelectionStatus.DirectoryMissing);
        trackName = string.Join("_", trackName.Split(Path.GetInvalidFileNameChars()))
            .Replace('\'', '_').Replace('`', '_');
        if (string.IsNullOrWhiteSpace(trackName) || trackName is "." or "..")
            return new(RBRPacenoteSelectionStatus.Unknown);
        var stage = Path.Combine(directory, trackName);
        if (!Directory.Exists(stage)) stage += " BTB";
        if (!Directory.Exists(stage))
            return new(RBRPacenoteSelectionStatus.StageMissing);

        var files = Directory.GetFiles(stage);
        var markers = files.Where(f => Path.GetFileName(f).StartsWith("mypacenote_", StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (markers.Length != 1)
            return new(RBRPacenoteSelectionStatus.Unknown);
        var selected = Path.GetFileNameWithoutExtension(markers[0])["mypacenote_".Length..];
        if (selected.Equals("default", StringComparison.OrdinalIgnoreCase))
            return new(RBRPacenoteSelectionStatus.DefaultSelected);
        if (selected.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            // Keep the existing newest-file ordering. A timestamp tie is not reliable evidence
            // of the loaded file, so report uncertainty without changing the legacy mapping.
            var candidates = files.Where(f => Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            if (candidates.Length == 0) return new(RBRPacenoteSelectionStatus.Unknown);
            if (candidates.Length > 1 && candidates[0].LastWriteTimeUtc == candidates[1].LastWriteTimeUtc)
                return new(RBRPacenoteSelectionStatus.Unknown, candidates[0].FullName);
            return FromFile(candidates[0].FullName);
        }
        var file = files.FirstOrDefault(f => Path.GetFileName(f).Equals(selected + ".ini", StringComparison.OrdinalIgnoreCase));
        return file == null ? new(RBRPacenoteSelectionStatus.Unknown) : FromFile(file);
    }

    private static RBRPacenoteSelection FromFile(string path) => new(
        Path.GetFileNameWithoutExtension(path).EndsWith("_default", StringComparison.OrdinalIgnoreCase)
            ? RBRPacenoteSelectionStatus.DefaultSelected : RBRPacenoteSelectionStatus.CustomSelected, path);
}
