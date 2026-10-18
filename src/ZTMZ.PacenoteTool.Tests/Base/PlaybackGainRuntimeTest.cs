using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Tests.Base;

[CollectionDefinition("PlaybackGainRuntime", DisableParallelization = true)]
public sealed class PlaybackGainRuntimeCollection;

[Collection("PlaybackGainRuntime")]
public sealed class PlaybackGainRuntimeTest : IDisposable
{
    private readonly FieldInfo configField = typeof(Config).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? previousConfig;
    private readonly string previousConfigPath;
    private readonly string root = Path.Combine(Path.GetTempPath(), "ztmz-gain-" + Guid.NewGuid());

    public PlaybackGainRuntimeTest()
    {
        previousConfig = configField.GetValue(null);
        previousConfigPath = Config.USER_CONFIG_FILE;
        configField.SetValue(null, Activator.CreateInstance(typeof(Config), nonPublic: true));
        Directory.CreateDirectory(root);
        Config.USER_CONFIG_FILE = Path.Combine(root, "userconfig.json");
        Config.Instance.UseDynamicVolume = false;
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(-6)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(24)]
    public void AudioSamplesUseTheDecibelGain(double db)
    {
        var sound = new AutoResampledCachedSound([.05f, -.05f]) { Gain = PlaybackGain.ToLinear(db) };
        Assert.Equal(.05f * PlaybackGain.ToLinear(db), sound[0]);
        Assert.Equal(-.05f * PlaybackGain.ToLinear(db), sound[1]);
        sound.Amplification = -1000;
        Assert.Equal(0f, sound[0]);
        sound.Amplification = 500;
        Assert.Equal(.3f, sound[0], 6);
    }

    [Fact]
    public void RuntimePersistsAndAppliesGainWithoutChangingMuteAndRollsBackFailedSave()
    {
        var tool = (ZTMZPacenoteTool)RuntimeHelpers.GetUninitializedObject(typeof(ZTMZPacenoteTool));
        var profile = (ProfileManager)RuntimeHelpers.GetUninitializedObject(typeof(ProfileManager));
        typeof(ZTMZPacenoteTool).GetField("_profileManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tool, profile);
        var controls = new SharedRuntimeControlsService(tool, new NoDashboard());
        Config.Instance.UI_Mute = true;
        Assert.True(controls.SetPlaybackGainDb(-6));
        Assert.Equal(-6, profile.CurrentPlaybackGainDb);
        using (var saved = JsonDocument.Parse(File.ReadAllText(Config.USER_CONFIG_FILE)))
        {
            Assert.Equal(-6, saved.RootElement.GetProperty("UI_PlaybackGainDb").GetDouble());
            Assert.True(saved.RootElement.GetProperty("UI_Mute").GetBoolean());
        }
        Assert.True(controls.SetPlaybackGainDb(0));
        Assert.Equal(0, profile.CurrentPlaybackGainDb);
        Assert.True(Config.Instance.UI_Mute);
        Config.USER_CONFIG_FILE = root; // A directory cannot be overwritten as a settings file.
        Assert.False(controls.SetPlaybackGainDb(12));
        Assert.Equal(0, Config.Instance.UI_PlaybackGainDb);
        Assert.Equal(0, profile.CurrentPlaybackGainDb);
    }

    public void Dispose()
    {
        Config.USER_CONFIG_FILE = previousConfigPath;
        configField.SetValue(null, previousConfig);
        Directory.Delete(root, true);
    }

    private sealed class NoDashboard : IRuntimeDashboardController
    {
        public event EventHandler? StatusChanged { add { } remove { } }
        public RuntimeDashboardStatus Status => new("Stopped", 0, "", "", false);
        public RuntimeDashboardDiagnostics Diagnostics => new(Status, 0, 0, 0, null);
        public void Configure(bool enabled, int port, int targetFrameRate) { }
    }
}
