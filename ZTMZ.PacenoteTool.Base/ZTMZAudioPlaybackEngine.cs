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
        private readonly MixingSampleProvider mixer;
        private readonly SequentialSampleProvider sequential;
        private readonly bool _isSequential;


        public ZTMZAudioPlaybackEngine(int deviceID = -1, bool isSequential = true, int desiredLatency = 50,
            int sampleRate = 44100, int channelCount = 2)
        {
            outputDeviceMixer = new WaveOutEvent();
            outputDeviceSequential = new WaveOutEvent();
            var ieeeFloatWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
            mixer = new MixingSampleProvider(ieeeFloatWaveFormat);
            mixer.ReadFully = true;
            sequential = new SequentialSampleProvider(ieeeFloatWaveFormat);
            sequential.ReadFully = true;
            outputDeviceMixer.DeviceNumber = deviceID;
            outputDeviceMixer.DesiredLatency = desiredLatency;
            outputDeviceSequential.DeviceNumber = deviceID;
            outputDeviceSequential.DesiredLatency = desiredLatency;
            _isSequential = isSequential;

            outputDeviceMixer.Init(mixer);
            outputDeviceSequential.Init(sequential);
            outputDeviceMixer.Play();
            outputDeviceSequential.Play();
        }

        public void PlaySound(string fileName, bool isSequential = true)
        {
            // var input = new AudioFileReader(fileName);
            // AddMixerInput(new AutoDisposeFileReader(input), isSequential);
            this.PlaySound(new AutoResampledCachedSound(fileName), isSequential);
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

        public void PlaySound(AutoResampledCachedSound sound, bool isSequential = true)
        {
            AddMixerInput(
                new VariSpeedSampleProvider(
                    new AutoResampledCachedSoundSampleProvider(sound), 
                    500, 
                    this.PlaybackRate, Config.Instance.UseTempoInsteadOfRate), 
                isSequential);            
            // AddMixerInput(
            //         new AutoResampledCachedSoundSampleProvider(sound), 
            //     isSequential);
        }

        private void AddMixerInput(ISampleProvider input, bool isSequential = true)
        {
            if (isSequential)
            {
                sequential.AddSequentialInput(ConvertToRightChannelCount(input));
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
        }

        public float PlaybackRate { set; get; } = Config.Instance.UI_PlaybackSpeed;
    }
}