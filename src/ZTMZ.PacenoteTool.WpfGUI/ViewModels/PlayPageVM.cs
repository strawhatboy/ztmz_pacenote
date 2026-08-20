
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.UI.Windows.Game;
using Wpf.Ui.Controls;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class PlayPageVM : ObservableObject {
    [ObservableProperty]
    private bool _useDynamicPlaybackSpeed = PlaybackSettingsService.Instance.UseDynamicPlaybackSpeed;

    partial void OnUseDynamicPlaybackSpeedChanged(bool value)
    {
        PlaybackSettingsService.Instance.UseDynamicPlaybackSpeed = value;
    }

    [ObservableProperty]
    private float _dynamicPlaybackMaxSpeed = PlaybackSettingsService.Instance.DynamicPlaybackMaxSpeed;

    partial void OnDynamicPlaybackMaxSpeedChanged(float value)
    {
        PlaybackSettingsService.Instance.DynamicPlaybackMaxSpeed = value;
    }

    [ObservableProperty]
    private bool _useDynamicVolume = PlaybackSettingsService.Instance.UseDynamicVolume;

    partial void OnUseDynamicVolumeChanged(bool value)
    {
        PlaybackSettingsService.Instance.UseDynamicVolume = value;
    }

    [ObservableProperty]
    private int _dynamicVolumePerturbationFrequency = PlaybackSettingsService.Instance.DynamicVolumePerturbationFrequency;

    partial void OnDynamicVolumePerturbationFrequencyChanged(int value)
    {
        PlaybackSettingsService.Instance.DynamicVolumePerturbationFrequency = value;
    }

    [ObservableProperty]
    private float _dynamicVolumePerturbationAmplitude = PlaybackSettingsService.Instance.DynamicVolumePerturbationAmplitude;

    partial void OnDynamicVolumePerturbationAmplitudeChanged(float value)
    {
        PlaybackSettingsService.Instance.DynamicVolumePerturbationAmplitude = value;
    }

    [ObservableProperty]
    private int _playbackDeviceDesiredLatency = PlaybackSettingsService.Instance.PlaybackDeviceDesiredLatency;

    partial void OnPlaybackDeviceDesiredLatencyChanged(int value)
    {
        PlaybackSettingsService.Instance.PlaybackDeviceDesiredLatency = value;
    }

    [ObservableProperty]
    private bool _connectCloseDistanceCallToNextPacenote = PlaybackSettingsService.Instance.ConnectCloseDistanceCallToNextPacenote;

    partial void OnConnectCloseDistanceCallToNextPacenoteChanged(bool value)
    {
        PlaybackSettingsService.Instance.ConnectCloseDistanceCallToNextPacenote = value;
    }

    [ObservableProperty]
    private bool _connectNumericDistanceCallToPreviousPacenote = PlaybackSettingsService.Instance.ConnectNumericDistanceCallToPreviousPacenote;

    partial void OnConnectNumericDistanceCallToPreviousPacenoteChanged(bool value)
    {
        PlaybackSettingsService.Instance.ConnectNumericDistanceCallToPreviousPacenote = value;
    }

    [ObservableProperty]
    private string _examplePacenoteString = PlaybackSettingsService.Instance.ExamplePacenoteString;

    partial void OnExamplePacenoteStringChanged(string value)
    {
        PlaybackSettingsService.Instance.ExamplePacenoteString = value;
    }
}
