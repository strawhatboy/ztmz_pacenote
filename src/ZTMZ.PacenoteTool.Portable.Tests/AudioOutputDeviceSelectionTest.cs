using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class AudioOutputDeviceSelectionTest
{
    private static readonly AudioOutputDeviceInfo[] Devices =
    [
        new("16", "Headphones", true),
        new("14", "Monitor", false),
        new("17", "Streaming Microphone", false),
        new("15", "Streaming Speakers", false)
    ];

    [Fact]
    public void ExistingDeviceId_RemainsSelected()
    {
        Assert.Equal(
            1,
            ZtmzRuntime.ResolveConfiguredOutputDeviceIndex(Devices, "14", 3));
    }

    [Fact]
    public void StaleDeviceId_FallsBackToCurrentDefault()
    {
        Assert.Equal(
            0,
            ZtmzRuntime.ResolveConfiguredOutputDeviceIndex(Devices, "3", 3));
    }

    [Fact]
    public void MissingDeviceId_UsesLegacyIndexForMigration()
    {
        Assert.Equal(
            2,
            ZtmzRuntime.ResolveConfiguredOutputDeviceIndex(Devices, "", 2));
    }

    [Fact]
    public void Refresh_PreservesCurrentDeviceByStableId()
    {
        var reordered = new[] { Devices[3], Devices[1], Devices[0], Devices[2] };

        Assert.Equal(
            1,
            ZTMZPacenoteTool.FindRefreshedOutputDeviceIndex(reordered, Devices[1]));
    }

    [Fact]
    public void Refresh_FallsBackToDefaultWhenCurrentDeviceWasRemoved()
    {
        Assert.Equal(
            0,
            ZTMZPacenoteTool.FindRefreshedOutputDeviceIndex(
                Devices,
                new AudioOutputDeviceInfo("missing", "Removed DAC", false)));
    }

    [Fact]
    public void Refresh_DoesNotSwitchToNewDefaultWhileCurrentDeviceStillExists()
    {
        AudioOutputDeviceInfo[] changedDefault =
        [
            new("14", "Monitor", true),
            new("16", "Headphones", false)
        ];

        Assert.Equal(
            1,
            ZTMZPacenoteTool.FindRefreshedOutputDeviceIndex(
                changedDefault,
                Devices[0]));
    }
}
