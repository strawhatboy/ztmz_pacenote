#nullable enable

using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.UI.Windows;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

public sealed record RuntimeControlsSnapshot(
    IReadOnlyList<CoDriverPackageInfo> Codrivers,
    CoDriverPackageInfo? SelectedCodriver,
    IReadOnlyList<AudioOutputDeviceInfo> OutputDevices,
    AudioOutputDeviceInfo? SelectedOutputDevice,
    bool IsStageActive,
    bool IsMuted,
    bool IsNativeHudVisible,
    bool IsWebDashboardEnabled,
    RuntimeDashboardStatus WebDashboardStatus)
{
    public double PlaybackGainDb { get; init; }

    public TrayQuickControlMenuState ToTrayMenuState()
    {
        return TrayQuickControlMenuBuilder.Build(
            Codrivers,
            SelectedCodriver == null
                ? string.Empty
                : CoDriverSelectionIdentity.Create(SelectedCodriver),
            IsStageActive,
            IsMuted,
            IsNativeHudVisible,
            IsWebDashboardEnabled);
    }
}

public sealed class RuntimeControlsService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly SharedRuntimeControlsService _sharedControls;
    private readonly GameOverlayManager _gameOverlayManager;

    public RuntimeControlsService(
        SharedRuntimeControlsService sharedControls,
        GameOverlayManager gameOverlayManager)
    {
        _sharedControls = sharedControls;
        _gameOverlayManager = gameOverlayManager;
        _sharedControls.StateChanged += (_, _) => RaiseStateChanged();
    }

    public event EventHandler? StateChanged;

    public RuntimeControlsSnapshot GetSnapshot()
    {
        var shared = _sharedControls.GetSnapshot();
        return new RuntimeControlsSnapshot(
            shared.Codrivers,
            shared.SelectedCodriver,
            shared.OutputDevices,
            shared.SelectedOutputDevice,
            shared.IsStageActive,
            shared.IsMuted,
            Config.Instance.UI_ShowHud,
            shared.IsWebDashboardEnabled,
            shared.WebDashboardStatus) { PlaybackGainDb = shared.PlaybackGainDb };
    }

    public bool TrySelectCodriver(string identity) =>
        _sharedControls.TrySelectCodriver(identity);

    public bool TrySelectOutputDevice(string id) =>
        _sharedControls.TrySelectOutputDevice(id);

    public bool TryPlayExample() => _sharedControls.TryPlayExample();

    public bool SetPlaybackGainDb(double decibels) => _sharedControls.SetPlaybackGainDb(decibels);

    public bool SetMuted(bool muted) => _sharedControls.SetMuted(muted);

    public bool SetWebDashboardEnabled(bool enabled) =>
        _sharedControls.SetWebDashboardEnabled(enabled);

    public bool SetNativeHudVisible(bool visible)
    {
        var previous = Config.Instance.UI_ShowHud;
        if (previous == visible)
        {
            _gameOverlayManager.RefreshNativeOverlayVisibility();
            return true;
        }

        Config.Instance.UI_ShowHud = visible;
        try
        {
            Config.Instance.SaveUserConfig();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist the native HUD setting.");
            Config.Instance.UI_ShowHud = previous;
            _gameOverlayManager.RefreshNativeOverlayVisibility();
            return false;
        }

        _gameOverlayManager.RefreshNativeOverlayVisibility();
        RaiseStateChanged();
        return true;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
