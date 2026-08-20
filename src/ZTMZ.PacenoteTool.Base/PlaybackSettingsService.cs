using System;

namespace ZTMZ.PacenoteTool.Base;

public sealed class PlaybackSettingsService
{
    public static PlaybackSettingsService Instance { get; } = new();

    private PlaybackSettingsService()
    {
    }

    public bool UseDynamicPlaybackSpeed
    {
        get => Config.Instance.UseDynamicPlaybackSpeed;
        set => Save(config => config.UseDynamicPlaybackSpeed = value);
    }

    public float DynamicPlaybackMaxSpeed
    {
        get => Config.Instance.DynamicPlaybackMaxSpeed;
        set => Save(config => config.DynamicPlaybackMaxSpeed = value);
    }

    public bool UseDynamicVolume
    {
        get => Config.Instance.UseDynamicVolume;
        set => Save(config => config.UseDynamicVolume = value);
    }

    public int DynamicVolumePerturbationFrequency
    {
        get => Config.Instance.DynamicVolumePerturbationFrequency;
        set => Save(config => config.DynamicVolumePerturbationFrequency = value);
    }

    public float DynamicVolumePerturbationAmplitude
    {
        get => Config.Instance.DynamicVolumePerturbationAmplitude;
        set => Save(config => config.DynamicVolumePerturbationAmplitude = value);
    }

    public int PlaybackDeviceDesiredLatency
    {
        get => Config.Instance.PlaybackDeviceDesiredLatency;
        set => Save(config => config.PlaybackDeviceDesiredLatency = value);
    }

    public bool ConnectCloseDistanceCallToNextPacenote
    {
        get => Config.Instance.ConnectCloseDistanceCallToNextPacenote;
        set => Save(config => config.ConnectCloseDistanceCallToNextPacenote = value);
    }

    public bool ConnectNumericDistanceCallToPreviousPacenote
    {
        get => Config.Instance.ConnectNumericDistanceCallToPreviousPacenote;
        set => Save(config => config.ConnectNumericDistanceCallToPreviousPacenote = value);
    }

    public string ExamplePacenoteString
    {
        get => Config.Instance.ExamplePacenoteString;
        set => Save(config => config.ExamplePacenoteString = value);
    }

    private static void Save(Action<Config> update)
    {
        update(Config.Instance);
        Config.Instance.SaveUserConfig();
    }
}
