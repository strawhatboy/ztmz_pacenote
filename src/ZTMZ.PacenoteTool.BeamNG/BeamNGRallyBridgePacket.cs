using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZTMZ.PacenoteTool.BeamNG;

public sealed class BeamNGRallyBridgePacket
{
    public const string MagicValue = "ZTMZ_BNG_RALLY";
    public const int SupportedProtocolVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string Magic { get; init; } = string.Empty;
    public int ProtocolVersion { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string VehicleName { get; init; } = string.Empty;
    public string VehicleClass { get; init; } = string.Empty;
    public BeamNGRallyVehicleTelemetry? Telemetry { get; init; }
    public BeamNGRallyDamage? Damage { get; init; }
    public BeamNGRallyStage? Stage { get; init; }
    public BeamNGRallyPacenote? Pacenote { get; init; }
    public List<BeamNGRallyPacenote>? Pacenotes { get; init; }

    public static bool TryParse(ReadOnlySpan<byte> message, out BeamNGRallyBridgePacket? packet)
    {
        packet = null;
        if (message.IsEmpty || !StartsWithJsonObject(message))
        {
            return false;
        }

        try
        {
            var parsedPacket = JsonSerializer.Deserialize<BeamNGRallyBridgePacket>(message, SerializerOptions);
            if (parsedPacket is null || !string.Equals(parsedPacket.Magic, MagicValue, StringComparison.Ordinal))
            {
                return false;
            }

            packet = parsedPacket;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool StartsWithJsonObject(ReadOnlySpan<byte> message)
    {
        foreach (var value in message)
        {
            if (!char.IsWhiteSpace((char)value))
            {
                return value == (byte)'{';
            }
        }

        return false;
    }
}

/// <summary>
/// Vehicle values which BeamNG's standard OutGauge packet does not expose.
/// </summary>
public sealed class BeamNGRallyVehicleTelemetry
{
    public int VehicleId { get; init; }
    public float MaxRPM { get; init; }
    public float IdleRPM { get; init; }
}

/// <summary>
/// Change-set data from BeamNG's built-in vehicle damage tracker.
/// </summary>
public sealed class BeamNGRallyDamage
{
    public int VehicleId { get; init; }
    public Dictionary<string, Dictionary<string, JsonElement>> Current { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, JsonElement>> Changed { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BeamNGRallyStage
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsCountingDown { get; init; }
    public int CountdownNumber { get; init; }
    public bool IsFinished { get; init; }
    public float StageTimeSeconds { get; init; }
    public float DistanceMeters { get; init; }
    public float TotalDistanceMeters { get; init; }
    public float Completion { get; init; }
}

public sealed class BeamNGRallyPacenote
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    /// <summary>
    /// The authored freeform source call when the stage uses a freeform notebook.
    /// This is distinct from <see cref="Text"/>, which is BeamNG's compiled display output.
    /// </summary>
    public string SourceText { get; init; } = string.Empty;
    /// <summary>
    /// The language key stored by the stage notebook for a freeform source call.
    /// </summary>
    public string SourceLanguage { get; init; } = string.Empty;
    public string AudioMode { get; init; } = string.Empty;
    /// <summary>
    /// BeamNG's language-neutral structured source, projected with the live road measurement.
    /// </summary>
    public BeamNGRallyPacenoteStructured? Structured { get; init; }
    public string Visual { get; init; } = string.Empty;
    public float DistanceBeforeMeters { get; init; }
    public float DistanceAfterMeters { get; init; }
    public float DistanceToMeters { get; init; }
}

/// <summary>
/// A snapshot of BeamNG's Rally Mode structured pacenote schema. The bridge exports
/// this instead of making the desktop app infer a corner from localized display text.
/// </summary>
public sealed class BeamNGRallyPacenoteStructured
{
    public int SchemaVersion { get; init; }
    public Dictionary<string, BeamNGRallyPacenoteStructuredItem> Items { get; init; } = new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>
/// A structured pacenote slot. BeamNG reserves slots 1-6 for caution, pre-corner
/// modifiers, the corner, and post-corner modifiers. Unknown future fields are kept
/// in the stage export so they are never discarded by an older desktop build.
/// </summary>
public sealed class BeamNGRallyPacenoteStructuredItem
{
    public string Type { get; init; } = string.Empty;
    public int? Level { get; init; }
    public int? Direction { get; init; }
    public float? Intensity { get; init; }
    public float? ArcMeters { get; init; }
    public string Descriptor { get; init; } = string.Empty;
    public string Shape { get; init; } = string.Empty;
    public string MeasurementType { get; init; } = string.Empty;
    public int? RiskIntensity { get; init; }
    public string LengthMode { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
