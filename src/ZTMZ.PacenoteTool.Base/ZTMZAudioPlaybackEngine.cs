using NAudio.Extras;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Runtime.Versioning;
using System.Threading;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Base
{
    [SupportedOSPlatform("windows")]
    public class ZTMZAudioPlaybackEngine : IDisposable
    {
        private readonly IAudioOutputBackend outputDevice;
        private readonly MixingSampleProvider mixer;
        private readonly VolumeSampleProvider outputVolume;
        private readonly SequentialSampleProvider sequential;
        private readonly SequentialSampleProvider sequential2;
        private readonly object diagnosticsLock = new();
        private readonly int deviceId;
        private readonly int desiredLatency;
        private string lastError = string.Empty;
        private long queuedSoundCount;
        private long lastSoundQueuedUtcTicks;
        private long lastErrorUtcTicks;
        private int isDisposed;


        public ZTMZAudioPlaybackEngine(string deviceEndpointId = "", int deviceID = 0,
            bool isSequential = true, int desiredLatency = 50,
            int sampleRate = 44100, int channelCount = 2)
        {
            this.deviceId = deviceID;
            this.desiredLatency = desiredLatency;
            outputDevice = new WasapiAudioOutputBackend(deviceEndpointId, desiredLatency);

            var ieeeFloatWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
            mixer = new MixingSampleProvider(ieeeFloatWaveFormat) { ReadFully = true };
            outputVolume = new VolumeSampleProvider(mixer);
            sequential = new SequentialSampleProvider(ieeeFloatWaveFormat) { ReadFully = true };
            sequential2 = new SequentialSampleProvider(ieeeFloatWaveFormat) { ReadFully = true };

            // Keep one device alive and mix independent logical lanes into it. This avoids
            // separate WaveOut streams competing for the same device during a new callout.
            mixer.AddMixerInput(sequential);
            mixer.AddMixerInput(sequential2);
            mixer.AddMixerInput(new AudioKeepAliveSampleProvider(ieeeFloatWaveFormat));

            SetMuted(Config.Instance.UI_Mute);
            outputDevice.Start(outputVolume);
        }

        public bool IsMuted { get; private set; }

        public void SetMuted(bool muted)
        {
            IsMuted = muted;
            outputVolume.Volume = muted ? 0f : 1f;
        }

        public AudioPlaybackStatus Status
        {
            get
            {
                lock (diagnosticsLock)
                {
                    return new AudioPlaybackStatus(
                        Volatile.Read(ref isDisposed) == 0,
                        deviceId,
                        desiredLatency,
                        Interlocked.Read(ref queuedSoundCount),
                        ToUtcDateTimeOffset(Interlocked.Read(ref lastSoundQueuedUtcTicks)),
                        ToUtcDateTimeOffset(Interlocked.Read(ref lastErrorUtcTicks)),
                        lastError);
                }
            }
        }

        public void PlaySound(string fileName, bool isSequential = true, bool isSystem = false)
        {
            // var input = new AudioFileReader(fileName);
            // AddMixerInput(new AutoDisposeFileReader(input), isSequential);
            this.PlaySound(new AutoResampledCachedSound(fileName), isSequential, isSystem);
        }

        private ISampleProvider ConvertToRightChannelCount(ISampleProvider input)
        {
            if (input.WaveFormat.Channels == mixer.WaveFormat.Channels)
            {
                return input;
            }

            if (input.WaveFormat.Channels == 1 && mixer.WaveFormat.Channels == 2)
            {
                return new MonoToStereoSampleProvider(input);
            }

            throw new NotImplementedException("Not yet implemented this channel count conversion");
        }

        public void PlaySound(AutoResampledCachedSound sound, bool isSequential = true, bool isSystem = false)
        {
            try
            {
                AddMixerInput(
                    new InterComEffectSampleProvider(
                        new VariSpeedSampleProvider(
                            new AutoResampledCachedSoundSampleProvider(sound),
                            500,
                            this.PlaybackRate, Config.Instance.UseTempoInsteadOfRate),
                        Config.Instance.IntercomEffect),
                        isSequential, isSystem);
                Interlocked.Increment(ref queuedSoundCount);
                Interlocked.Exchange(ref lastSoundQueuedUtcTicks, DateTime.UtcNow.Ticks);
            }
            catch (Exception exception)
            {
                RecordError(exception);
                throw;
            }
            // AddMixerInput(
            //         new AutoResampledCachedSoundSampleProvider(sound), 
            //     isSequential);
        }

        private void AddMixerInput(ISampleProvider input, bool isSequential = true, bool isSystem = false)
        {
            if (isSequential)
            {
                if (isSystem)
                {
                    sequential2.AddSequentialInput(ConvertToRightChannelCount(input));
                }
                else
                {
                    sequential.AddSequentialInput(ConvertToRightChannelCount(input));
                }
            }
            else
            {
                mixer.AddMixerInput(ConvertToRightChannelCount(input));
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isDisposed, 1) != 0)
            {
                return;
            }

            try
            {
                outputDevice.Dispose();
            }
            catch (Exception exception)
            {
                RecordError(exception);
                throw;
            }
        }

        private void RecordError(Exception exception)
        {
            lock (diagnosticsLock)
            {
                lastError = $"{exception.GetType().Name}: {exception.Message}";
                Interlocked.Exchange(ref lastErrorUtcTicks, DateTime.UtcNow.Ticks);
            }
        }

        private static DateTimeOffset? ToUtcDateTimeOffset(long ticks)
        {
            return ticks == 0
                ? null
                : new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
        }

        public float PlaybackRate { set; get; } = Config.Instance.UI_PlaybackSpeed;
    }
}
