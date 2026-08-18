using Xunit;
using ZTMZ.PacenoteTool.Audio.PortAudio;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class PortAudioOutputSafetyTest
{
    [Fact]
    public void SanitizeOutputSamples_ClampsInvalidAndOutOfRangeValues()
    {
        float[] samples =
        [
            -2f,
            -1f,
            -0.25f,
            0f,
            0.25f,
            1f,
            2f,
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity
        ];

        var corrected = PortAudioAudioOutputBackend.SanitizeOutputSamples(samples);

        Assert.Equal(5, corrected);
        Assert.Equal([-1f, -1f, -0.25f, 0f, 0.25f, 1f, 1f, 0f, 0f, 0f], samples);
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("pipewire", true)]
    [InlineData("pulse", true)]
    [InlineData("sysdefault", true)]
    [InlineData("Ensoniq AudioPCI: ES1371 DAC1 (hw:0,1)", true)]
    [InlineData("lavrate", false)]
    [InlineData("samplerate", false)]
    [InlineData("speexrate", false)]
    [InlineData("upmix", false)]
    [InlineData("vdownmix", false)]
    public void LinuxDeviceFilter_OnlyKeepsUserFacingOutputs(string name, bool expected)
    {
        Assert.Equal(
            expected,
            PortAudioAudioOutputDeviceEnumerator.IsLinuxUserFacingDeviceName(name));
    }

    [Fact]
    public void StableDeviceId_DoesNotDependOnPortAudioListIndex()
    {
        var hostApi = new PortAudioHostApiInfo(3, 13, "Windows WASAPI", 16);

        Assert.Equal(
            "portaudio:13:Headphones%20%28USB%20DAC%29",
            PortAudioAudioOutputDeviceEnumerator.CreateStableDeviceId(
                hostApi,
                "Headphones (USB DAC)"));
    }

    [Theory]
    [InlineData("pipewire", 0)]
    [InlineData("pulse", 1)]
    [InlineData("default", 2)]
    [InlineData("sysdefault", 3)]
    [InlineData("Ensoniq AudioPCI: ES1371 DAC2/ADC (hw:0,0)", 10)]
    public void LinuxDevicePriority_PrefersDesktopAudioServer(
        string name,
        int expectedPriority)
    {
        Assert.Equal(
            expectedPriority,
            PortAudioAudioOutputDeviceEnumerator.GetLinuxDevicePriority(name));
    }
}
