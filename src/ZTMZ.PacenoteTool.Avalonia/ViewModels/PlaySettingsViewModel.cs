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

    [ObservableProperty]
    private bool _useDynamicPlaybackSpeed = PlaybackSettingsService.Instance.UseDynamicPlaybackSpeed;

    [ObservableProperty]
    private float _dynamicPlaybackMaxSpeed = PlaybackSettingsService.Instance.DynamicPlaybackMaxSpeed;

    [ObservableProperty]
    private bool _useDynamicVolume = PlaybackSettingsService.Instance.UseDynamicVolume;

    [ObservableProperty]
    private int _dynamicVolumePerturbationFrequency = PlaybackSettingsService.Instance.DynamicVolumePerturbationFrequency;

    [ObservableProperty]
    private float _dynamicVolumePerturbationAmplitude = PlaybackSettingsService.Instance.DynamicVolumePerturbationAmplitude;

    [ObservableProperty]
    private int _playbackDeviceDesiredLatency = PlaybackSettingsService.Instance.PlaybackDeviceDesiredLatency;

    [ObservableProperty]
    private bool _connectCloseDistanceCallToNextPacenote = PlaybackSettingsService.Instance.ConnectCloseDistanceCallToNextPacenote;

    [ObservableProperty]
    private bool _connectNumericDistanceCallToPreviousPacenote = PlaybackSettingsService.Instance.ConnectNumericDistanceCallToPreviousPacenote;

    [ObservableProperty]
    private string _examplePacenoteString = PlaybackSettingsService.Instance.ExamplePacenoteString;

    partial void OnUseDynamicPlaybackSpeedChanged(bool value)
    {
        if (_initializing) return;
        PlaybackSettingsService.Instance.UseDynamicPlaybackSpeed = value;
    }

    partial void OnDynamicPlaybackMaxSpeedChanged(float value)
    {
        if (_initializing) return;
        PlaybackSettingsService.Instance.DynamicPlaybackMaxSpeed = value;
    }

    partial void OnUseDynamicVolumeChanged(bool value)
    {
        if (_initializing) return;
        PlaybackSettingsService.Instance.UseDynamicVolume = value;
    }

    partial void OnDynamicVolumePerturbationFrequencyChanged(int value)
    {
        if (_initializing) return;
        PlaybackSettingsService.Instance.DynamicVolumePerturbationFrequency = value;
    }

    partial void OnDynamicVolumePerturbationAmplitudeChanged(float value)
    {
        if (_initializing) return;
        PlaybackSettingsService.Instance.DynamicVolumePerturbationAmplitude = value;
    }

    partial void OnPlaybackDeviceDesiredLatencyChanged(int value)
    {
        if (_initializing) return;
        PlaybackSettingsService.Instance.PlaybackDeviceDesiredLatency = value;
    }

    partial void OnConnectCloseDistanceCallToNextPacenoteChanged(bool value)
    {
        if (_initializing) return;
        PlaybackSettingsService.Instance.ConnectCloseDistanceCallToNextPacenote = value;
    }

    partial void OnConnectNumericDistanceCallToPreviousPacenoteChanged(bool value)
    {
        if (_initializing) return;
        PlaybackSettingsService.Instance.ConnectNumericDistanceCallToPreviousPacenote = value;
    }

    partial void OnExamplePacenoteStringChanged(string value)
    {
        if (_initializing) return;
        PlaybackSettingsService.Instance.ExamplePacenoteString = value;
    }

    public void Dispose()
    {
    }
}
