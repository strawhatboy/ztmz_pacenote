using System.Buffers.Binary;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZTMZ.PacenoteTool.AC;

internal enum ACRRelayFrameType : byte
{
    Hello = 1,
    Telemetry = 2,
    Pacenote = 3,
    Status = 4
}

internal readonly record struct ACRRelayFrame(ACRRelayFrameType Type, byte[] Payload);

internal static class ACRRelayProtocol
{
    public const byte Version = 1;
    public const int HeaderSize = 12;
    public const int GraphicsSize = 1588;
    public const int PhysicsSize = 800;
    public const int StaticSize = 820;
    public const int TelemetrySize = GraphicsSize + PhysicsSize + StaticSize;
    public const int MaximumPayloadSize = 1024 * 1024;

    private static ReadOnlySpan<byte> Magic => "ZAR1"u8;

    public static bool TryParseHeader(
        ReadOnlySpan<byte> header,
        out ACRRelayFrameType type,
        out int payloadLength)
    {
        type = default;
        payloadLength = 0;
        if (header.Length != HeaderSize || !header[..4].SequenceEqual(Magic) || header[4] != Version)
        {
            return false;
        }

        type = (ACRRelayFrameType)header[5];
        payloadLength = BinaryPrimitives.ReadInt32BigEndian(header[8..12]);
        return Enum.IsDefined(type) && payloadLength is >= 0 and <= MaximumPayloadSize;
    }

    public static async ValueTask<ACRRelayFrame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        if (!await ReadExactlyOrEndAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!TryParseHeader(header, out var type, out var payloadLength))
        {
            throw new InvalidDataException("The ACR Proton relay sent an invalid frame header.");
        }

        var payload = new byte[payloadLength];
        if (!await ReadExactlyOrEndAsync(stream, payload, cancellationToken).ConfigureAwait(false))
        {
            throw new EndOfStreamException("The ACR Proton relay disconnected during a frame.");
        }
        return new ACRRelayFrame(type, payload);
    }

    private static async ValueTask<bool> ReadExactlyOrEndAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset == 0 ? false : throw new EndOfStreamException();
            }
            offset += read;
        }
        return true;
    }
}
