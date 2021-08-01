using NAudio.Extras;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;

namespace ZTMZ.PacenoteTool
{
    public class ZTMZAudioPlaybackEngine : IDisposable
    {
        private readonly WaveOutEvent outputDevice;
        private readonly MixingSampleProvider mixer;
        private readonly SequentialSampleProvider sequential;
        private readonly bool _isSequential;


        public ZTMZAudioPlaybackEngine(int deviceID = -1, bool isSequential = true, int desiredLatency = 50, int sampleRate = 44100, int channelCount = 2)
        {
            outputDevice = new WaveOutEvent();
            var ieeeFloatWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
            mixer = new MixingSampleProvider(ieeeFloatWaveFormat);
            mixer.ReadFully = true;
            sequential = new SequentialSampleProvider(ieeeFloatWaveFormat);
            sequential.ReadFully = true;
            outputDevice.DeviceNumber = deviceID;
            outputDevice.DesiredLatency = desiredLatency;
            _isSequential = isSequential;
            if (isSequential)
            {
                outputDevice.Init(sequential);
            } else
            {
                outputDevice.Init(mixer);
            }
            outputDevice.Play();
        }

        public void PlaySound(string fileName)
        {
            var input = new AudioFileReader(fileName);
            AddMixerInput(new AutoDisposeFileReader(input));
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

        public void PlaySound(AutoResampledCachedSound sound)
        {
            AddMixerInput(new AutoResampledCachedSoundSampleProvider(sound));
        }

        private void AddMixerInput(ISampleProvider input)
        {
            if (_isSequential)
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
            outputDevice.Dispose();
        }
    }
}
