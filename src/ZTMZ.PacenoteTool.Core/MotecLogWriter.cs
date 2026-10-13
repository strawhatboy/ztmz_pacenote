// Binary layout based on Aim_2_MoTeC (MIT, Ludovic Bouchard).
// See ThirdPartyNotices/Aim_2_MoTeC.txt. Unknown header bytes are zero, not copied from a real driver's log.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Core;

internal static class MotecLogWriter
{
    private const int HeaderSize = 1762;
    private const int EventSize = 1154;
    private const int ChannelSize = 124;

    public static void Write(Stream stream, Replay replay, MotecReplayExporter.Samples samples, CancellationToken cancellationToken)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var metadata = HeaderSize + EventSize;
        var data = checked(metadata + samples.Channels.Length * ChannelSize);
        var byteCount = checked(samples.Count * sizeof(float));
        stream.SetLength(checked((long)data + (long)byteCount * samples.Channels.Length));
        w.Write(0x40);
        w.Seek(8, SeekOrigin.Begin);
        w.Write(metadata); w.Write(data);
        w.Seek(36, SeekOrigin.Begin); w.Write(HeaderSize);
        w.Seek(64, SeekOrigin.Begin);
        w.Write((ushort)0); w.Write((ushort)0x4240); w.Write((ushort)0xf);
        w.Write(12007); Text(w, "ADL", 8); w.Write((ushort)420); w.Write((ushort)0x80);
        w.Write(samples.Channels.Length); w.Write(0x10064);
        Text(w, replay.date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), 16);
        w.Seek(126, SeekOrigin.Begin); Text(w, replay.date.ToString("HH:mm:ss", CultureInfo.InvariantCulture), 16);
        w.Seek(158, SeekOrigin.Begin); Text(w, "ZTMZ", 64); Text(w, replay.car, 64);
        w.Seek(350, SeekOrigin.Begin); Text(w, replay.track, 64);
        w.Seek(1502, SeekOrigin.Begin); w.Write(0xD20822);
        w.Seek(1572, SeekOrigin.Begin); Text(w, "ZTMZ replay telemetry", 64);
        w.Seek(HeaderSize, SeekOrigin.Begin);
        Text(w, replay.track, 64); Text(w, "ZTMZ replay", 64);
        Text(w, $"Resampled recorded telemetry at {samples.Frequency} Hz. Optional all-zero channels omitted. " + replay.comment, 1024);
        w.Write((ushort)0); // no venue structure

        for (int i = 0; i < samples.Channels.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = samples.Channels[i];
            w.Seek(metadata + i * ChannelSize, SeekOrigin.Begin);
            w.Write(i == 0 ? 0 : metadata + (i - 1) * ChannelSize);
            w.Write(i == samples.Channels.Length - 1 ? 0 : metadata + (i + 1) * ChannelSize);
            w.Write(checked(data + i * byteCount)); w.Write(samples.Count);
            w.Write((ushort)(0x2ee1 + i)); w.Write((ushort)7); w.Write((ushort)4); w.Write(samples.Frequency);
            w.Write((short)0); w.Write((short)1); w.Write((short)1); w.Write((short)0);
            Text(w, channel.Name, 32); Text(w, "", 8); Text(w, channel.Unit, 12);
            w.Write(new byte[40]);
            w.Seek(checked(data + i * byteCount), SeekOrigin.Begin);
            int n = 0;
            foreach (var value in MotecReplayExporter.Resample(samples, channel))
            {
                if ((n++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                w.Write(value);
            }
        }
    }

    private static void Text(BinaryWriter writer, string value, int size)
    {
        var bytes = new byte[size];
        // Preserve valid UTF-8 when a fixed-width field truncates a localized stage/car name.
        Encoding.UTF8.GetEncoder().Convert((value ?? "").AsSpan(), bytes.AsSpan(0, size - 1), true,
            out _, out _, out _);
        writer.Write(bytes);
    }
}
