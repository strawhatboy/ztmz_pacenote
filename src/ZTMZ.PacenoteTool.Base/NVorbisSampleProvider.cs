using System;
using NAudio.Wave;
using NVorbis;

namespace ZTMZ.PacenoteTool.Base;

public sealed class NVorbisSampleProvider : ISampleProvider, IDisposable
{
    private readonly VorbisReader reader;
    private bool isDisposed;

    public NVorbisSampleProvider(string fileName)
    {
        reader = new VorbisReader(fileName);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(reader.SampleRate, reader.Channels);
    }

    public WaveFormat WaveFormat { get; }

    public long TotalSamples => reader.TotalSamples;

    public int Read(Span<float> buffer)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var alignedLength = buffer.Length - buffer.Length % WaveFormat.Channels;
        return alignedLength == 0 ? 0 : reader.ReadSamples(buffer[..alignedLength]);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        reader.Dispose();
    }
}
