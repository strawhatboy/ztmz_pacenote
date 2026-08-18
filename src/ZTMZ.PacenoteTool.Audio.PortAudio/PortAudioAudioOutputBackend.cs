using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PortAudioSharp;
using ZTMZ.PacenoteTool.Base;
using PortAudioStream = PortAudioSharp.Stream;

namespace ZTMZ.PacenoteTool.Audio.PortAudio;

public sealed class PortAudioAudioOutputBackend : IAudioOutputBackend
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly Exception OutputUnderflowError = new InvalidOperationException(
        "PortAudio reported an output underflow; increase the playback latency if it repeats.");
    private readonly IDisposable runtime;
    private readonly int channelCount;
    private readonly int outputSampleRate;
    private readonly PortAudioStream.Callback callback;
    private PortAudioStream? stream;
    private ISampleProvider? source;
    private Exception? playbackError;
    private long lastErrorUtcTicks;
    private long correctedSampleCount;
    private long outputUnderflowCount;
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
            var deviceIndex = ResolveDeviceIndex(deviceId, channelCount);
            var device = global::PortAudioSharp.PortAudio.GetDeviceInfo(deviceIndex);
            if (device.maxOutputChannels < channelCount)
            {
                throw new InvalidOperationException(
                    $"PortAudio device '{device.name}' supports {device.maxOutputChannels} output " +
                    $"channels, but {channelCount} are required.");
            }

            outputSampleRate = ResolveOutputSampleRate(
                deviceIndex,
                device,
                sampleRate,
                channelCount);

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
                outputSampleRate,
                global::PortAudioSharp.PortAudio.FramesPerBufferUnspecified,
                StreamFlags.NoFlag,
                callback,
                this);

            DeviceId = deviceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DeviceName = device.name;
            LatencyMilliseconds = desiredLatencyMilliseconds;

            Logger.Info(
                "Created PortAudio output for {0} ({1}) at {2} Hz with {3} ms desired latency.",
                DeviceName,
                DeviceId,
                outputSampleRate,
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

        this.source = source.WaveFormat.SampleRate == outputSampleRate
            ? source
            : new WdlResamplingSampleProvider(source, outputSampleRate);

        if (source.WaveFormat.SampleRate != outputSampleRate)
        {
            Logger.Info(
                "Resampling PortAudio output from {0} Hz to the device rate {1} Hz for {2}.",
                source.WaveFormat.SampleRate,
                outputSampleRate,
                DeviceName);
        }

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

            var underflows = Interlocked.Read(ref outputUnderflowCount);
            if (underflows > 0)
            {
                Logger.Warn("PortAudio reported {0} output underflows for {1}.", underflows, DeviceName);
            }

            var correctedSamples = Interlocked.Read(ref correctedSampleCount);
            if (correctedSamples > 0)
            {
                Logger.Warn(
                    "PortAudio corrected {0} non-finite or out-of-range output samples for {1}.",
                    correctedSamples,
                    DeviceName);
            }
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
            if ((statusFlags & StreamCallbackFlags.OutputUnderflow) != 0)
            {
                Interlocked.Increment(ref outputUnderflowCount);
                RecordPlaybackError(OutputUnderflowError);
            }

            var samplesRead = source?.Read(outputSamples) ?? 0;
            Interlocked.Add(
                ref correctedSampleCount,
                SanitizeOutputSamples(outputSamples[..samplesRead]));
            outputSamples[samplesRead..].Clear();
            return StreamCallbackResult.Continue;
        }
        catch (Exception exception)
        {
            outputSamples.Clear();
            RecordPlaybackError(exception);
            return StreamCallbackResult.Abort;
        }
    }

    internal static int SanitizeOutputSamples(Span<float> samples)
    {
        var corrected = 0;
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            if (!float.IsFinite(sample))
            {
                samples[index] = 0f;
                corrected++;
            }
            else if (sample > 1f)
            {
                samples[index] = 1f;
                corrected++;
            }
            else if (sample < -1f)
            {
                samples[index] = -1f;
                corrected++;
            }
        }

        return corrected;
    }

    private void RecordPlaybackError(Exception exception)
    {
        Volatile.Write(ref playbackError, exception);
        Interlocked.Exchange(ref lastErrorUtcTicks, DateTime.UtcNow.Ticks);
    }

    private static int ResolveOutputSampleRate(
        int deviceIndex,
        DeviceInfo device,
        int sourceSampleRate,
        int channelCount)
    {
        var defaultSampleRate = double.IsFinite(device.defaultSampleRate) &&
                                device.defaultSampleRate > 0 &&
                                device.defaultSampleRate <= int.MaxValue
            ? checked((int)Math.Round(device.defaultSampleRate))
            : 0;
        if (defaultSampleRate > 0 &&
            PortAudioNative.IsOutputFormatSupported(
                deviceIndex,
                device,
                defaultSampleRate,
                channelCount))
        {
            return defaultSampleRate;
        }

        if (PortAudioNative.IsOutputFormatSupported(
                deviceIndex,
                device,
                sourceSampleRate,
                channelCount))
        {
            return sourceSampleRate;
        }

        throw new InvalidOperationException(
            $"PortAudio device '{device.name}' does not support {channelCount}-channel float output " +
            $"at its default rate ({device.defaultSampleRate:G} Hz) or the source rate " +
            $"({sourceSampleRate} Hz).");
    }

    private static int ResolveDeviceIndex(string deviceId, int requiredOutputChannels)
    {
        var hostApis = PortAudioNative.GetHostApis();
        var hostApisByIndex = hostApis.ToDictionary(hostApi => hostApi.Index);
        for (var index = 0; index < global::PortAudioSharp.PortAudio.DeviceCount; index++)
        {
            var device = global::PortAudioSharp.PortAudio.GetDeviceInfo(index);
            if (device.maxOutputChannels >= requiredOutputChannels &&
                hostApisByIndex.TryGetValue(device.hostApi, out var hostApi) &&
                string.Equals(
                    PortAudioAudioOutputDeviceEnumerator.CreateStableDeviceId(
                        hostApi,
                        device.name),
                    deviceId,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

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

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            throw new InvalidOperationException(
                $"PortAudio output device '{deviceId}' is no longer available.");
        }

        deviceIndex = global::PortAudioSharp.PortAudio.DefaultOutputDevice;
        if (deviceIndex == global::PortAudioSharp.PortAudio.NoDevice)
        {
            throw new InvalidOperationException("PortAudio did not report a default output device.");
        }

        return deviceIndex;
    }
}
