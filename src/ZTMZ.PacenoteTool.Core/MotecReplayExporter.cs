using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Core;

public sealed record MotecReplayExportResult(string LogPath, string IndexPath, int Frequency, int SampleCount);

/// <summary>Exports recorded samples, resampled on stage time, without synthesizing missing telemetry.</summary>
public static class MotecReplayExporter
{
    internal sealed record Channel(string Name, string Unit, Func<ReplayDetailsPerTime, float> Read, bool Step = false);
    internal sealed record Samples(ReplayDetailsPerTime[] Source, ushort Frequency, int Count, Channel[] Channels);

    public static MotecReplayExportResult Export(Replay replay, IReadOnlyList<ReplayDetailsPerTime> details,
        string destinationDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(details);
        cancellationToken.ThrowIfCancellationRequested();
        if (!replay.available) throw new InvalidOperationException("The replay is no longer available.");
        var samples = Prepare(details);
        // A separate directory also prevents overwriting an existing .ldx containing the user's i2 annotations.
        var directory = Path.Combine(Path.GetFullPath(destinationDirectory), $"MoTeC-{replay.id}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, "telemetry.ld");
        var index = Path.Combine(directory, "telemetry.ldx");
        try
        {
            using (var output = new FileStream(log, FileMode.CreateNew, FileAccess.Write))
                MotecLogWriter.Write(output, replay, samples, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // These are recording boundaries, not invented finish/lap timing. Retired and partial
            // recordings must not be labelled a completed lap or a fastest lap.
            var markers = new XElement("MarkerGroup", new XAttribute("Name", "Beacons"), new XAttribute("Index", 3));
            if (!replay.retired && samples.Source[0].time == 0 && replay.finish_time > 0 &&
                Math.Abs((samples.Count - 1d) / samples.Frequency - replay.finish_time) <= 0.001)
            {
                markers.Add(Beacon(0));
                markers.Add(Beacon(replay.finish_time));
            }
            new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("LDXFile", new XAttribute("Locale", "English_United States.1252"),
                    new XAttribute("DefaultLocale", "C"), new XAttribute("Version", "1.6"),
                    new XElement("Layers", new XElement("Layer", new XElement("MarkerBlock", markers), new XElement("RangeBlock")))))
                .Save(index);
            cancellationToken.ThrowIfCancellationRequested();
            return new(log, index, samples.Frequency, samples.Count);
        }
        catch
        {
            // Only files created by this export are removed; never touch an existing export.
            File.Delete(log);
            File.Delete(index);
            Directory.Delete(directory);
            throw;
        }
    }

    private static XElement Beacon(double time) => new("Marker", new XAttribute("Version", 100),
        new XAttribute("ClassName", "BCN"), new XAttribute("Name", ""), new XAttribute("Flags", 77),
        new XAttribute("Time", (time * 1_000_000).ToString("R", CultureInfo.InvariantCulture)));

    internal static Samples Prepare(IReadOnlyList<ReplayDetailsPerTime> details)
    {
        var source = new List<ReplayDetailsPerTime>(details.Count);
        foreach (var item in details)
        {
            // ReplayManager inserts an empty start sample (timestamp 0). It is not a measurement.
            if (item.timestamp == 0 && item.time == 0 && item.distance == 0 && item.speed == 0 && item.rpm == 0) continue;
            if (!float.IsFinite(item.time)) throw new InvalidDataException("Replay contains an invalid stage time.");
            if (item.time < 0) continue; // countdown
            if (source.Count > 0)
            {
                if (item.time < source[^1].time) throw new InvalidDataException("Replay stage time runs backwards; cannot export a single MoTeC session.");
                if (item.time == source[^1].time) { source[^1] = item; continue; }
            }
            source.Add(item);
        }
        if (source.Count < 2) throw new InvalidDataException("Replay has fewer than two timed samples.");
        var intervals = source.Zip(source.Skip(1), (a, b) => (double)b.time - a.time).OrderBy(x => x).ToArray();
        var median = intervals[intervals.Length / 2];
        if (intervals[^1] > Math.Max(5, median * 10))
            throw new InvalidDataException("Replay has a long telemetry gap; export would invent data across the gap.");
        var frequency = (ushort)Math.Clamp(Math.Round(1 / median), 1, 1000);
        var countDouble = Math.Floor(((double)source[^1].time - source[0].time) * frequency + 0.0001) + 1;
        if (countDouble < 2 || countDouble > 5_000_000)
            throw new InvalidDataException("Replay duration or sample count is not supported for MoTeC export.");

        var channels = new List<Channel>
        {
            new("Stage Time", "s", x => x.time),
            new("Distance", "m", x => x.distance)
        };
        // Old replays have no telemetry-presence flags. Only emit optional channels with
        // nonzero recorded values; a missing channel is preferable to false all-zero telemetry.
        var optional = new Channel[]
        {
            new("Ground Speed", "km/h", x => x.speed),
            new("Engine RPM", "rpm", x => x.rpm),
            new("Gear", "", x => x.gear, true),
            new("Throttle Pos", "%", x => x.throttle * 100),
            new("Brake Pos", "%", x => x.brake * 100),
            new("Clutch Pos", "%", x => x.clutch * 100),
            new("Handbrake Pos", "%", x => x.handbrake * 100),
            new("Steering", "%", x => x.steering * 100),
            new("Position X", "m", x => x.pos_x),
            new("Position Y", "m", x => x.pos_y),
            new("Position Z", "m", x => x.pos_z)
        };
        channels.AddRange(optional.Where(c => source.Any(s => c.Read(s) != 0)));
        foreach (var channel in channels)
            if (source.Any(s => !float.IsFinite(channel.Read(s))))
                throw new InvalidDataException($"Replay channel {channel.Name} contains invalid values.");
        return new(source.ToArray(), frequency, (int)countDouble, channels.ToArray());
    }

    internal static IEnumerable<float> Resample(Samples samples, Channel channel)
    {
        var source = samples.Source;
        int left = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            double time = source[0].time + (double)i / samples.Frequency;
            while (left + 1 < source.Length && source[left + 1].time <= time + 0.00001) left++;
            var a = channel.Read(source[left]);
            if (channel.Step || left == source.Length - 1) { yield return a; continue; }
            var b = channel.Read(source[left + 1]);
            var ratio = Math.Clamp((time - source[left].time) / (source[left + 1].time - source[left].time), 0, 1);
            yield return (float)(a + ((double)b - a) * ratio);
        }
    }
}
