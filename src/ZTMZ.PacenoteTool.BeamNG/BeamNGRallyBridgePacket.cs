using System;
using System.Collections.Generic;
using System.Text.Json;

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

public sealed class BeamNGRallyStage
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsCountingDown { get; init; }
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
    public string Visual { get; init; } = string.Empty;
    public float DistanceBeforeMeters { get; init; }
    public float DistanceAfterMeters { get; init; }
    public float DistanceToMeters { get; init; }
}
