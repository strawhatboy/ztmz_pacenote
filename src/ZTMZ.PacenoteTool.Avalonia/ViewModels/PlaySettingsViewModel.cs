#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Avalonia.ViewModels;

internal sealed partial class PlaySettingsViewModel : ObservableObject, IDisposable
{
    private bool _initializing = true;

    public PlaySettingsViewModel()
    {
        _initializing = false;
    }

    // UseSequentialMixerToHandleAudioConflict and UseTempoInsteadOfRate are
    // intentionally not exposed: they are always true (see Config defaults) and
    // the audio engine relies on those defaults, so no UI is needed for them.

    [ObservableProperty]
    private bool _useDynamicPlaybackSpeed = Config.Instance.UseDynamicPlaybackSpeed;

    [ObservableProperty]
    private float _dynamicPlaybackMaxSpeed = Config.Instance.DynamicPlaybackMaxSpeed;

    [ObservableProperty]
    private bool _useDynamicVolume = Config.Instance.UseDynamicVolume;

    [ObservableProperty]
    private int _dynamicVolumePerturbationFrequency = Config.Instance.DynamicVolumePerturbationFrequency;

    [ObservableProperty]
    private float _dynamicVolumePerturbationAmplitude = Config.Instance.DynamicVolumePerturbationAmplitude;

    [ObservableProperty]
    private int _playbackDeviceDesiredLatency = Config.Instance.PlaybackDeviceDesiredLatency;

    [ObservableProperty]
    private bool _connectCloseDistanceCallToNextPacenote = Config.Instance.ConnectCloseDistanceCallToNextPacenote;

    [ObservableProperty]
    private bool _connectNumericDistanceCallToPreviousPacenote = Config.Instance.ConnectNumericDistanceCallToPreviousPacenote;

    [ObservableProperty]
    private string _examplePacenoteString = Config.Instance.ExamplePacenoteString;

    partial void OnUseDynamicPlaybackSpeedChanged(bool value)
    {
        if (_initializing) return;
        Config.Instance.UseDynamicPlaybackSpeed = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnDynamicPlaybackMaxSpeedChanged(float value)
    {
        if (_initializing) return;
        Config.Instance.DynamicPlaybackMaxSpeed = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnUseDynamicVolumeChanged(bool value)
    {
        if (_initializing) return;
        Config.Instance.UseDynamicVolume = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnDynamicVolumePerturbationFrequencyChanged(int value)
    {
        if (_initializing) return;
        Config.Instance.DynamicVolumePerturbationFrequency = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnDynamicVolumePerturbationAmplitudeChanged(float value)
    {
        if (_initializing) return;
        Config.Instance.DynamicVolumePerturbationAmplitude = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnPlaybackDeviceDesiredLatencyChanged(int value)
    {
        if (_initializing) return;
        Config.Instance.PlaybackDeviceDesiredLatency = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnConnectCloseDistanceCallToNextPacenoteChanged(bool value)
    {
        if (_initializing) return;
        Config.Instance.ConnectCloseDistanceCallToNextPacenote = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnConnectNumericDistanceCallToPreviousPacenoteChanged(bool value)
    {
        if (_initializing) return;
        Config.Instance.ConnectNumericDistanceCallToPreviousPacenote = value;
        Config.Instance.SaveUserConfig();
    }

    partial void OnExamplePacenoteStringChanged(string value)
    {
        if (_initializing) return;
        Config.Instance.ExamplePacenoteString = value;
        Config.Instance.SaveUserConfig();
    }

    public void Dispose()
    {
    }
}
