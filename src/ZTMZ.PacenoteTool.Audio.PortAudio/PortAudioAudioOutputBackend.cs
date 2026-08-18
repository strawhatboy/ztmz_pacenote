using System.Runtime.InteropServices;
using NAudio.Wave;
using PortAudioSharp;
using ZTMZ.PacenoteTool.Base;
using PortAudioStream = PortAudioSharp.Stream;

namespace ZTMZ.PacenoteTool.Audio.PortAudio;

public sealed class PortAudioAudioOutputBackend : IAudioOutputBackend
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly IDisposable runtime;
    private readonly int channelCount;
    private readonly PortAudioStream.Callback callback;
    private PortAudioStream? stream;
    private ISampleProvider? source;
    private Exception? playbackError;
    private long lastErrorUtcTicks;
    private int isDisposed;

    public PortAudioAudioOutputBackend(
        string deviceId,
        int desiredLatencyMilliseconds,
        int sampleRate,
        int channelCount)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

        runtime = PortAudioRuntime.Acquire();
        this.channelCount = channelCount;
        callback = FillOutputBuffer;

        try
        {
            var deviceIndex = ResolveDeviceIndex(deviceId);
            var device = global::PortAudioSharp.PortAudio.GetDeviceInfo(deviceIndex);
            if (device.maxOutputChannels < channelCount)
            {
                throw new InvalidOperationException(
                    $"PortAudio device '{device.name}' supports {device.maxOutputChannels} output " +
                    $"channels, but {channelCount} are required.");
            }

            var outputParameters = new StreamParameters
            {
                device = deviceIndex,
                channelCount = channelCount,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = Math.Max(
                    desiredLatencyMilliseconds / 1000d,
                    device.defaultLowOutputLatency),
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            stream = new PortAudioStream(
                null,
                outputParameters,
                sampleRate,
                global::PortAudioSharp.PortAudio.FramesPerBufferUnspecified,
                StreamFlags.ClipOff,
                callback,
                this);

            DeviceId = deviceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DeviceName = device.name;
            LatencyMilliseconds = desiredLatencyMilliseconds;

            Logger.Info(
                "Created PortAudio output for {0} ({1}) with {2} ms desired latency.",
                DeviceName,
                DeviceId,
                LatencyMilliseconds);
        }
        catch
        {
            stream?.Dispose();
            runtime.Dispose();
            throw;
        }
    }

    public string DeviceId { get; }

    public string DeviceName { get; }

    public int LatencyMilliseconds { get; }

    public DateTimeOffset? LastErrorUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref lastErrorUtcTicks);
            return ticks == 0
                ? null
                : new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
        }
    }

    public string LastError
    {
        get
        {
            var error = Volatile.Read(ref playbackError);
            return error == null
                ? string.Empty
                : $"{error.GetType().Name}: {error.Message}";
        }
    }

    public void Start(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);

        if (source.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat ||
            source.WaveFormat.Channels != channelCount)
        {
            throw new ArgumentException(
                "PortAudio output requires interleaved IEEE float samples with the configured channel count.",
                nameof(source));
        }

        this.source = source;
        stream!.Start();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (stream is { IsActive: true })
            {
                stream.Abort();
            }

            stream?.Dispose();
        }
        finally
        {
            stream = null;
            source = null;
            runtime.Dispose();
        }
    }

    private unsafe StreamCallbackResult FillOutputBuffer(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userDataPtr)
    {
        var sampleCount = checked((int)frameCount * channelCount);
        var outputSamples = new Span<float>(output.ToPointer(), sampleCount);

        try
        {
            var samplesRead = source?.Read(outputSamples) ?? 0;
            outputSamples[samplesRead..].Clear();
            return StreamCallbackResult.Continue;
        }
        catch (Exception exception)
        {
            outputSamples.Clear();
            Volatile.Write(ref playbackError, exception);
            Interlocked.Exchange(ref lastErrorUtcTicks, DateTime.UtcNow.Ticks);
            return StreamCallbackResult.Abort;
        }
    }

    private static int ResolveDeviceIndex(string deviceId)
    {
        if (int.TryParse(
                deviceId,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var deviceIndex) &&
            deviceIndex >= 0 &&
            deviceIndex < global::PortAudioSharp.PortAudio.DeviceCount)
        {
            return deviceIndex;
        }

        deviceIndex = global::PortAudioSharp.PortAudio.DefaultOutputDevice;
        if (deviceIndex == global::PortAudioSharp.PortAudio.NoDevice)
        {
            throw new InvalidOperationException("PortAudio did not report a default output device.");
        }

        return deviceIndex;
    }
}
