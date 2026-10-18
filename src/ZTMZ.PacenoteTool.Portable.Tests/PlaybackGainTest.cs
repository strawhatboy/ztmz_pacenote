using System.Reflection;
using System.Text.Json;
using Xunit;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class PlaybackGainTest
{
    [Theory]
    [InlineData(-60, .001)]
    [InlineData(-6.020599913, .5)]
    [InlineData(0, 1)]
    [InlineData(6.020599913, 2)]
    [InlineData(24, 15.848931925)]
    public void DecibelsMapToSignalAmplitude(double db, double linear) =>
        Assert.InRange(Math.Abs(PlaybackGain.ToLinear(db) - linear), 0, 0.000002);

    [Theory]
    [InlineData(-100, -60)]
    [InlineData(100, 24)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    public void InvalidOrOutOfRangeGainIsNormalized(double input, double expected) =>
        Assert.Equal(expected, PlaybackGain.NormalizeDb(input));

    [Theory]
    [InlineData(-999.9)]
    [InlineData(-999)]
    [InlineData(-500)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void MigrationPreservesOldPlaybackAmplitudeAndPersistsUnroundedGain(double legacy)
    {
        var config = ReadConfig(JsonSerializer.Serialize(new { UI_PlaybackVolume = legacy }));
        Assert.Equal(PlaybackGain.LegacyToLinear(legacy), PlaybackGain.ToLinear(config.UI_PlaybackGainDb));
        Assert.False(config.UI_Mute);

        var json = (string)typeof(Config).GetMethod("SerializeUserConfigSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(config, null)!;
        Assert.Contains("UI_PlaybackGainDb", json);
        var reloaded = ReadConfig(json);
        Assert.Equal(config.UI_PlaybackGainDb, reloaded.UI_PlaybackGainDb);
        Assert.Equal(config.UI_Mute, reloaded.UI_Mute);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldZeroVolumeStaysSilentRegardlessOfOldMuteSetting(bool muted)
    {
        var config = ReadConfig(JsonSerializer.Serialize(new { UI_PlaybackVolume = -1000, UI_Mute = muted }));
        Assert.True(config.UI_Mute);
        Assert.Equal(0, config.UI_PlaybackGainDb);
    }

    [Fact]
    public void DecibelSettingTakesPrecedenceOverLegacyValueInEitherJsonOrder()
    {
        foreach (var json in new[]
        {
            "{\"UI_PlaybackVolume\":-1000,\"UI_PlaybackGainDb\":-6,\"UI_Mute\":false}",
            "{\"UI_PlaybackGainDb\":-6,\"UI_Mute\":false,\"UI_PlaybackVolume\":-1000}"
        })
        {
            var config = ReadConfig(json);
            Assert.Equal(-6, config.UI_PlaybackGainDb);
            Assert.False(config.UI_Mute);
        }
    }

    [Fact]
    public void DefaultAndResetGainAreUnityWithoutUnmuting()
    {
        Assert.Equal(0, ReadConfig("{}").UI_PlaybackGainDb);
        var config = ReadConfig("{\"UI_PlaybackVolume\":500,\"UI_Mute\":true}");
        Assert.True(config.UI_Mute);
        config.UI_PlaybackGainDb = 0;
        Assert.True(config.UI_Mute);
        Assert.Equal(1f, PlaybackGain.ToLinear(config.UI_PlaybackGainDb));
    }

    private static Config ReadConfig(string json)
    {
        var config = (Config)Activator.CreateInstance(typeof(Config), nonPublic: true)!;
        config.MergeFromUserConfig(JsonSerializer.Deserialize<Dictionary<string, dynamic>>(json)!);
        return config;
    }
}
