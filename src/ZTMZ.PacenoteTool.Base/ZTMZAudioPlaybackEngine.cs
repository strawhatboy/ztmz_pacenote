using NAudio.Extras;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Base
{
    public class ZTMZAudioPlaybackEngine : IDisposable
    {
        private readonly WaveOutEvent outputDeviceMixer;
        private readonly WaveOutEvent outputDeviceSequential;

        // for sequential system sound
        private readonly WaveOutEvent outputDeviceSequential2;
        private readonly MixingSampleProvider mixer;
        private readonly SequentialSampleProvider sequential;
        private readonly SequentialSampleProvider sequential2;
        private readonly bool _isSequential;


        public ZTMZAudioPlaybackEngine(int deviceID = -1, bool isSequential = true, int desiredLatency = 50,
            int sampleRate = 44100, int channelCount = 2)
        {
            outputDeviceMixer = new WaveOutEvent();
            outputDeviceSequential = new WaveOutEvent();
            outputDeviceSequential2 = new WaveOutEvent();

            var ieeeFloatWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
            mixer = new MixingSampleProvider(ieeeFloatWaveFormat);
            mixer.ReadFully = true;
            sequential = new SequentialSampleProvider(ieeeFloatWaveFormat);
            sequential.ReadFully = true;
            sequential2 = new SequentialSampleProvider(ieeeFloatWaveFormat);
            sequential2.ReadFully = true;

            outputDeviceMixer.DeviceNumber = deviceID;
            outputDeviceMixer.DesiredLatency = desiredLatency;
            outputDeviceSequential.DeviceNumber = deviceID;
            outputDeviceSequential.DesiredLatency = desiredLatency;
            outputDeviceSequential2.DeviceNumber = deviceID;
            outputDeviceSequential2.DesiredLatency = desiredLatency;

            _isSequential = isSequential;

            outputDeviceMixer.Init(mixer);
            outputDeviceMixer.Play();

            outputDeviceSequential.Init(sequential);
            outputDeviceSequential.Play();

            outputDeviceSequential2.Init(sequential2);
            outputDeviceSequential2.Play();
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
            AddMixerInput(
                new InterComEffectSampleProvider(
                    new VariSpeedSampleProvider(
                        new AutoResampledCachedSoundSampleProvider(sound), 
                        500, 
                        this.PlaybackRate, Config.Instance.UseTempoInsteadOfRate), 
                    Config.Instance.IntercomEffect),
                    isSequential, isSystem);
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
            outputDeviceMixer.Dispose();
            outputDeviceSequential.Dispose();
            outputDeviceSequential2.Dispose();
        }

        public float PlaybackRate { set; get; } = Config.Instance.UI_PlaybackSpeed;
    }
}
