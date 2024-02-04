
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.UI.Game;
using Wpf.Ui.Controls;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class PlayPageVM : ObservableObject {
    [ObservableProperty]
    private bool _useSequentialMixerToHandleAudioConflict = Config.Instance.UseSequentialMixerToHandleAudioConflict;

    partial void OnUseSequentialMixerToHandleAudioConflictChanged(bool value)
    {
        Config.Instance.UseSequentialMixerToHandleAudioConflict = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _useDynamicPlaybackSpeed = Config.Instance.UseDynamicPlaybackSpeed;

    partial void OnUseDynamicPlaybackSpeedChanged(bool value)
    {
        Config.Instance.UseDynamicPlaybackSpeed = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _useTempoInsteadOfRate = Config.Instance.UseTempoInsteadOfRate;

    partial void OnUseTempoInsteadOfRateChanged(bool value)
    {
        Config.Instance.UseTempoInsteadOfRate = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _dynamicPlaybackMaxSpeed = Config.Instance.DynamicPlaybackMaxSpeed;

    partial void OnDynamicPlaybackMaxSpeedChanged(float value)
    {
        Config.Instance.DynamicPlaybackMaxSpeed = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _useDynamicVolume = Config.Instance.UseDynamicVolume;

    partial void OnUseDynamicVolumeChanged(bool value)
    {
        Config.Instance.UseDynamicVolume = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private int _dynamicVolumePerturbationFrequency = Config.Instance.DynamicVolumePerturbationFrequency;

    partial void OnDynamicVolumePerturbationFrequencyChanged(int value)
    {
        Config.Instance.DynamicVolumePerturbationFrequency = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _dynamicVolumePerturbationAmplitude = Config.Instance.DynamicVolumePerturbationAmplitude;

    partial void OnDynamicVolumePerturbationAmplitudeChanged(float value)
    {
        Config.Instance.DynamicVolumePerturbationAmplitude = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private int _playbackDeviceDesiredLatency = Config.Instance.PlaybackDeviceDesiredLatency;

    partial void OnPlaybackDeviceDesiredLatencyChanged(int value)
    {
        Config.Instance.PlaybackDeviceDesiredLatency = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _connectCloseDistanceCallToNextPacenote = Config.Instance.ConnectCloseDistanceCallToNextPacenote;

    partial void OnConnectCloseDistanceCallToNextPacenoteChanged(bool value)
    {
        Config.Instance.ConnectCloseDistanceCallToNextPacenote = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _connectNumericDistanceCallToPreviousPacenote = Config.Instance.ConnectNumericDistanceCallToPreviousPacenote;

    partial void OnConnectNumericDistanceCallToPreviousPacenoteChanged(bool value)
    {
        Config.Instance.ConnectNumericDistanceCallToPreviousPacenote = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private string _examplePacenoteString = Config.Instance.ExamplePacenoteString;

    partial void OnExamplePacenoteStringChanged(string value)
    {
        Config.Instance.ExamplePacenoteString = value;
        Config.Instance.SaveUserConfig();
    }
}
