using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class MotecReplayExporterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ztmz-motec-" + Guid.NewGuid().ToString("N"));
    private static ReplayDetailsPerTime Sample(float time, float gear = 2) => new()
    { time = time, timestamp = 638000000000000000 + (long)(time * TimeSpan.TicksPerSecond), distance = time * 10,
        speed = 36, rpm = 3000, throttle = .5f, steering = -.2f, gear = gear };

    [Fact]
    public void BinaryFileHasLinkedFloatChannelsCorrectUnitsAndFiveHzSampling()
    {
        var replay = new Replay { id = 1, track = "赛道 & stage", car = "Car", date = new DateTime(2026, 10, 13), finish_time = .6f };
        var result = MotecReplayExporter.Export(replay, new[] { new ReplayDetailsPerTime(), Sample(0), Sample(.2f), Sample(.4f, 3), Sample(.6f, 3) }, directory);
        Assert.Equal(5, result.Frequency); Assert.Equal(4, result.SampleCount);
        // Read the binary independently of the production writer and validate pointer chains.
        using var reader = new BinaryReader(File.OpenRead(result.LogPath));
        Assert.Equal(0x40, reader.ReadInt32());
        reader.BaseStream.Position = 8;
        var next = reader.ReadInt32(); var dataStart = reader.ReadInt32();
        reader.BaseStream.Position = 86; var channelCount = reader.ReadInt32();
        var channels = new Dictionary<string, (string Unit, float[] Values)>();
        int previous = 0;
        while (next != 0)
        {
            var at = next; reader.BaseStream.Position = at;
            Assert.Equal(previous, reader.ReadInt32()); next = reader.ReadInt32();
            var data = reader.ReadInt32(); var count = reader.ReadInt32();
            Assert.InRange(data, dataStart, (int)reader.BaseStream.Length - count * 4);
            reader.ReadUInt16(); Assert.Equal(7, reader.ReadUInt16()); Assert.Equal(4, reader.ReadUInt16());
            Assert.Equal(5, reader.ReadUInt16());
            Assert.Equal(0, reader.ReadInt16()); Assert.Equal(1, reader.ReadInt16()); Assert.Equal(1, reader.ReadInt16()); Assert.Equal(0, reader.ReadInt16());
            var name = Encoding.UTF8.GetString(reader.ReadBytes(32)).TrimEnd('\0');
            reader.ReadBytes(8); var unit = Encoding.UTF8.GetString(reader.ReadBytes(12)).TrimEnd('\0');
            reader.BaseStream.Position = data;
            channels.Add(name, (unit, Enumerable.Range(0, count).Select(_ => reader.ReadSingle()).ToArray()));
            previous = at;
            Assert.True(channels.Count <= channelCount);
        }
        Assert.Equal(channelCount, channels.Count);
        Assert.Equal("km/h", channels["Ground Speed"].Unit);
        Assert.All(channels["Ground Speed"].Values, x => Assert.Equal(36, x));
        Assert.All(channels["Throttle Pos"].Values, x => Assert.Equal(50, x));
        Assert.All(channels["Steering"].Values, x => Assert.Equal(-20, x));
        Assert.Equal(new float[] { 2, 2, 3, 3 }, channels["Gear"].Values);
        Assert.False(channels.ContainsKey("Position X"));
        Assert.Equal(2, XDocument.Load(result.IndexPath).Descendants("Marker").Count());
        // Optional manual fixture export for independent parser / i2 validation.
        var fixture = Environment.GetEnvironmentVariable("ZTMZ_MOTEC_TEST_FIXTURE");
        if (!string.IsNullOrWhiteSpace(fixture))
        {
            Directory.CreateDirectory(fixture);
            File.Copy(result.LogPath, Path.Combine(fixture, "telemetry.ld"), true);
            File.Copy(result.IndexPath, Path.Combine(fixture, "telemetry.ldx"), true);
        }
    }

    [Fact]
    public void IrregularSamplingInterpolatesContinuousValuesButNeverFractionalGear()
    {
        var samples = new[] { Sample(10), Sample(10.18f), Sample(10.41f, 3), Sample(10.60f, 3) };
        samples[1].speed = 40; samples[2].speed = 50;
        var prepared = MotecReplayExporter.Prepare(samples);
        Assert.Equal(5, prepared.Frequency);
        Assert.Equal(new float[] { 2, 2, 2, 3 }, MotecReplayExporter.Resample(prepared, prepared.Channels.Single(c => c.Name == "Gear")));
        Assert.InRange(MotecReplayExporter.Resample(prepared, prepared.Channels.Single(c => c.Name == "Ground Speed")).ElementAt(1), 40, 42);
    }

    [Fact]
    public void DuplicateTimesUseLastValueAndOnlyDistanceIsExportedForTimingOnlyReplays()
    {
        var prepared = MotecReplayExporter.Prepare(new[] {
            new ReplayDetailsPerTime { time = 0, timestamp = 1 },
            new ReplayDetailsPerTime { time = 0, timestamp = 2, distance = 5 },
            new ReplayDetailsPerTime { time = .2f, timestamp = 3, distance = 7 } });
        Assert.Equal(2, prepared.Channels.Length);
        Assert.Equal(5, prepared.Source[0].distance);
    }

    [Fact]
    public void InvalidTimelinesAndNonFiniteTelemetryAreRejectedBeforeCreatingFiles()
    {
        foreach (var samples in new[] { Array.Empty<ReplayDetailsPerTime>(), new[] { Sample(1) },
            new[] { Sample(2), Sample(1) }, new[] { Sample(0), Sample(.2f), Sample(.4f), Sample(20) },
            new[] { Sample(0), Sample(float.NaN) } })
            Assert.Throws<InvalidDataException>(() => MotecReplayExporter.Export(new Replay(), samples, directory));
        var invalid = Sample(.2f); invalid.speed = float.PositiveInfinity;
        Assert.Throws<InvalidDataException>(() => MotecReplayExporter.Export(new Replay(), new[] { Sample(0), invalid }, directory));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void PartialRetiredRecordingsHaveNoInventedLapAndRepeatedExportsDoNotOverwrite()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var replay = new Replay { retired = true, finish_time = 20, date = DateTime.Now };
            var result = MotecReplayExporter.Export(replay, new[] { Sample(10), Sample(10.2f) }, directory);
            Assert.Empty(XDocument.Load(result.IndexPath).Descendants("Marker"));
            File.AppendAllText(result.IndexPath, "<!--user annotation-->");
            var other = MotecReplayExporter.Export(replay, new[] { Sample(10), Sample(10.2f) }, directory);
            Assert.NotEqual(other.LogPath, result.LogPath);
            Assert.Contains("user annotation", File.ReadAllText(result.IndexPath));
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    [Fact]
    public void CancellationDoesNotCreateAnExport()
    {
        Assert.Throws<OperationCanceledException>(() => MotecReplayExporter.Export(new Replay(), new[] { Sample(0), Sample(.2f) }, directory, new CancellationToken(true)));
        Assert.False(Directory.Exists(directory));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
