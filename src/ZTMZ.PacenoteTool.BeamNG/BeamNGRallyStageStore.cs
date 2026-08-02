using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.BeamNG;

public static class BeamNGRallyStageStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static string SaveStagePacenotes(BeamNGRallyStage? stage, IReadOnlyList<BeamNGRallyPacenote> pacenotes)
    {
        ArgumentNullException.ThrowIfNull(pacenotes);
        if (pacenotes.Count == 0)
        {
            throw new ArgumentException("A stage export must contain at least one pacenote.", nameof(pacenotes));
        }

        var directory = AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, "beamng", "rally-stages"));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, GetExportFileName(stage?.Id));
        var payload = new BeamNGRallyStageExport
        {
            ProtocolVersion = BeamNGRallyBridgePacket.SupportedProtocolVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            StageId = stage?.Id ?? string.Empty,
            StageName = stage?.Name ?? string.Empty,
            TotalDistanceMeters = stage?.TotalDistanceMeters ?? 0,
            Pacenotes = pacenotes.ToList()
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, SerializerOptions));
        return path;
    }

    public static string GetExportFileName(string? stageId)
    {
        var source = string.IsNullOrWhiteSpace(stageId) ? "unknown-stage" : stageId.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(source.Select(character =>
            invalidCharacters.Contains(character) || character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar
                ? '_'
                : character).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "unknown-stage.json" : sanitized + ".json";
    }
}

public sealed class BeamNGRallyStageExport
{
    public int ProtocolVersion { get; init; }
    public DateTimeOffset ExportedAtUtc { get; init; }
    public string StageId { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public float TotalDistanceMeters { get; init; }
    public List<BeamNGRallyPacenote> Pacenotes { get; init; } = [];
}
