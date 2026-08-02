using System;
using System.Buffers.Binary;
using System.Text;

namespace ZTMZ.PacenoteTool.BeamNG;

public sealed class BeamNGOutGaugePacket
{
    public const int MinimumPacketLength = 92;

    public string Car { get; init; } = string.Empty;
    public ushort Flags { get; init; }
    public int Gear { get; init; }
    public float SpeedMetersPerSecond { get; init; }
    public float Rpm { get; init; }
    public float TurboBar { get; init; }
    public float EngineTemperatureCelsius { get; init; }
    public float FuelFraction { get; init; }
    public float OilTemperatureCelsius { get; init; }
    public uint DashLights { get; init; }
    public uint ShowLights { get; init; }
    public float Throttle { get; init; }
    public float Brake { get; init; }
    public float Clutch { get; init; }

    public static bool TryParse(ReadOnlySpan<byte> message, out BeamNGOutGaugePacket? packet)
    {
        packet = null;
        if (message.Length < MinimumPacketLength || IsJson(message))
        {
            return false;
        }

        packet = new BeamNGOutGaugePacket
        {
            Car = ReadAscii(message, 4, 4),
            Flags = BinaryPrimitives.ReadUInt16LittleEndian(message.Slice(8, 2)),
            // OutGauge encodes reverse as 0, neutral as 1, and first as 2.
            Gear = message[10] - 1,
            SpeedMetersPerSecond = ReadSingle(message, 12),
            Rpm = ReadSingle(message, 16),
            TurboBar = ReadSingle(message, 20),
            EngineTemperatureCelsius = ReadSingle(message, 24),
            FuelFraction = ReadSingle(message, 28),
            OilTemperatureCelsius = ReadSingle(message, 36),
            DashLights = BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(40, 4)),
            ShowLights = BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(44, 4)),
            Throttle = ReadSingle(message, 48),
            Brake = ReadSingle(message, 52),
            Clutch = ReadSingle(message, 56)
        };

        return true;
    }

    private static bool IsJson(ReadOnlySpan<byte> message)
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

    private static float ReadSingle(ReadOnlySpan<byte> message, int offset)
    {
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(message.Slice(offset, 4)));
    }

    private static string ReadAscii(ReadOnlySpan<byte> message, int offset, int count)
    {
        return Encoding.ASCII.GetString(message.Slice(offset, count)).TrimEnd('\0', ' ');
    }
}
