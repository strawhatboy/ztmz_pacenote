using System.Runtime.InteropServices;
using PortAudioSharp;

namespace ZTMZ.PacenoteTool.Audio.PortAudio;

internal readonly record struct PortAudioHostApiInfo(
    int Index,
    int Type,
    string Name,
    int DefaultOutputDevice);

internal static class PortAudioNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeHostApiInfo
    {
        public int StructVersion;
        public int Type;
        public IntPtr Name;
        public int DeviceCount;
        public int DefaultInputDevice;
        public int DefaultOutputDevice;
    }

    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Pa_GetHostApiCount();

    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Pa_GetHostApiInfo(int hostApi);

    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)]
    private static extern ErrorCode Pa_IsFormatSupported(
        IntPtr inputParameters,
        ref StreamParameters outputParameters,
        double sampleRate);

    public static IReadOnlyList<PortAudioHostApiInfo> GetHostApis()
    {
        var count = Pa_GetHostApiCount();
        if (count < 0)
        {
            throw new InvalidOperationException(
                $"PortAudio host API discovery failed: " +
                global::PortAudioSharp.PortAudio.GetErrorText((ErrorCode)count));
        }

        var result = new List<PortAudioHostApiInfo>(count);
        for (var index = 0; index < count; index++)
        {
            var pointer = Pa_GetHostApiInfo(index);
            if (pointer == IntPtr.Zero)
            {
                continue;
            }

            var info = Marshal.PtrToStructure<NativeHostApiInfo>(pointer);
            result.Add(new PortAudioHostApiInfo(
                index,
                info.Type,
                Marshal.PtrToStringAnsi(info.Name) ?? $"Host API {index}",
                info.DefaultOutputDevice));
        }

        return result;
    }

    public static bool IsOutputFormatSupported(
        int deviceIndex,
        DeviceInfo device,
        double sampleRate,
        int channelCount)
    {
        if (device.maxOutputChannels < channelCount ||
            !double.IsFinite(sampleRate) ||
            sampleRate <= 0)
        {
            return false;
        }

        var parameters = new StreamParameters
        {
            device = deviceIndex,
            channelCount = channelCount,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = device.defaultLowOutputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };
        return Pa_IsFormatSupported(
            IntPtr.Zero,
            ref parameters,
            sampleRate) == ErrorCode.NoError;
    }
}
