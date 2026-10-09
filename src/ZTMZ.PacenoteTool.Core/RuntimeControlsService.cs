#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public sealed record RuntimeDashboardStatus(
    string State,
    int Port,
    string Url,
    string ErrorMessage,
    bool IsRunning);

public sealed record RuntimeDashboardDiagnostics(
    RuntimeDashboardStatus Status,
    int ConnectedClientCount,
    int TargetFrameRate,
    long RenderedFrameCount,
    DateTimeOffset? LastRenderedFrameUtc);

public interface IRuntimeDashboardController
{
    event EventHandler? StatusChanged;

    RuntimeDashboardStatus Status { get; }

    RuntimeDashboardDiagnostics Diagnostics { get; }

    void Configure(bool enabled, int port, int targetFrameRate);
}

public sealed record SharedRuntimeControlsSnapshot(
    IReadOnlyList<CoDriverPackageInfo> Codrivers,
    CoDriverPackageInfo? SelectedCodriver,
    IReadOnlyList<AudioOutputDeviceInfo> OutputDevices,
    AudioOutputDeviceInfo? SelectedOutputDevice,
    bool IsStageActive,
    bool IsMuted,
    double PlaybackVolume,
    double PlaybackAdjustSeconds,
    float PlaybackSpeed,
    int SilenceRemovalLevel,
    int IntercomEffect,
    bool IsWebDashboardEnabled,
    RuntimeDashboardStatus WebDashboardStatus);

public sealed class SharedRuntimeControlsService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ZTMZPacenoteTool _tool;
    private readonly IRuntimeDashboardController _dashboardController;
    private readonly object _codriverSnapshotLock = new();
    private IReadOnlyList<CoDriverPackageInfo> _lastKnownCodrivers = [];
    private int _isStageActive;
    private int _isExamplePlaybackPending;

    public SharedRuntimeControlsService(
        ZTMZPacenoteTool tool,
        IRuntimeDashboardController dashboardController)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _dashboardController = dashboardController ??
            throw new ArgumentNullException(nameof(dashboardController));

        _tool.onToolInitialized += OnToolInitialized;
        _tool.onCodriversRefreshed += RaiseStateChanged;
        _tool.onOutputDevicesChanged += RaiseStateChanged;
        _tool.onRaceBegin += _ => SetStageActive(true);
        _tool.onRaceEnd += _ => SetStageActive(false);
        _tool.onGameEnded += _ => SetStageActive(false);
        _tool.onGameUninitialized += _ => SetStageActive(false);
        _tool.onGameStateChanged += (_, state) =>
            SetStageActive(GameStateStageActivity.IsActive(state.NewGameState));
        _dashboardController.StatusChanged += (_, _) => RaiseStateChanged();
    }

    public event EventHandler? StateChanged;

    public SharedRuntimeControlsSnapshot GetSnapshot()
    {
        var codrivers = GetCodriverSnapshot();
        var selectedIndex = CoDriverSelectionIdentity.ResolveIndex(
            codrivers,
            Config.Instance.UI_SelectedAudioPackageIdentity,
            Config.Instance.UI_SelectedAudioPackage);
        var outputDevices = _tool.OutputDevices.ToArray();
        var selectedOutputIndex = Array.FindIndex(
            outputDevices,
            device => string.Equals(
                device.Id,
                Config.Instance.UI_SelectedPlaybackDeviceId,
                StringComparison.OrdinalIgnoreCase));
        if (selectedOutputIndex < 0 && outputDevices.Length > 0)
        {
            selectedOutputIndex = Math.Clamp(
                Config.Instance.UI_SelectedPlaybackDevice,
                0,
                outputDevices.Length - 1);
        }

        return new SharedRuntimeControlsSnapshot(
            codrivers,
            selectedIndex >= 0 ? codrivers[selectedIndex] : null,
            outputDevices,
            selectedOutputIndex >= 0 ? outputDevices[selectedOutputIndex] : null,
            IsStageActive,
            Config.Instance.UI_Mute,
            Math.Clamp((Config.Instance.UI_PlaybackVolume + 1000d) / 20d, 0d, 100d),
            Config.Instance.UI_PlaybackAdjustSeconds,
            Config.Instance.UI_PlaybackSpeed,
            GetSilenceRemovalLevel(Config.Instance.FactorToRemoveSpaceFromAudioFiles),
            Config.Instance.IntercomEffect,
            Config.Instance.WebDashboardEnabled,
            _dashboardController.Status);
    }

    public bool TrySelectOutputDevice(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var outputDevices = _tool.OutputDevices;
        var selectedIndex = outputDevices.FindIndex(device =>
            string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
        {
            return false;
        }

        var previousIndex = Config.Instance.UI_SelectedPlaybackDevice;
        var previousId = Config.Instance.UI_SelectedPlaybackDeviceId;
        Config.Instance.UI_SelectedPlaybackDevice = selectedIndex;
        Config.Instance.UI_SelectedPlaybackDeviceId = outputDevices[selectedIndex].Id;
        _tool.SetOutputDevice(selectedIndex);
        if (!TrySaveConfig("selected output device"))
        {
            Config.Instance.UI_SelectedPlaybackDevice = previousIndex;
            Config.Instance.UI_SelectedPlaybackDeviceId = previousId;
            _tool.SetOutputDevice(previousIndex);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool SetPlaybackVolume(double volume)
    {
        var normalized = Math.Clamp(volume, 0d, 100d);
        var previous = Config.Instance.UI_PlaybackVolume;
        Config.Instance.UI_PlaybackVolume = normalized * 20d - 1000d;
        _tool.SetPlaybackVolume((int)Config.Instance.UI_PlaybackVolume);
        if (!TrySaveConfig("playback volume"))
        {
            Config.Instance.UI_PlaybackVolume = previous;
            _tool.SetPlaybackVolume((int)previous);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool SetPlaybackAdjustSeconds(double seconds)
    {
        var normalized = Math.Clamp(seconds, -8d, 4d);
        var previous = Config.Instance.UI_PlaybackAdjustSeconds;
        Config.Instance.UI_PlaybackAdjustSeconds = normalized;
        _tool.SetPlaybackAdjustSeconds((float)normalized);
        if (!TrySaveConfig("playback timing"))
        {
            Config.Instance.UI_PlaybackAdjustSeconds = previous;
            _tool.SetPlaybackAdjustSeconds((float)previous);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool SetPlaybackSpeed(double speed)
    {
        var normalized = (float)Math.Clamp(speed, 0.5d, 1.5d);
        var previous = Config.Instance.UI_PlaybackSpeed;
        Config.Instance.UI_PlaybackSpeed = normalized;
        _tool.SetPlayBackSpeed(normalized);
        if (!TrySaveConfig("playback speed"))
        {
            Config.Instance.UI_PlaybackSpeed = previous;
            _tool.SetPlayBackSpeed(previous);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool SetSilenceRemovalLevel(int level)
    {
        var normalized = Math.Clamp(level, 0, 2);
        var previous = Config.Instance.FactorToRemoveSpaceFromAudioFiles;
        Config.Instance.FactorToRemoveSpaceFromAudioFiles = normalized switch
        {
            1 => 0.00005f,
            2 => 0.0001f,
            _ => 0.00001f
        };
        if (!TrySaveConfig("silence removal"))
        {
            Config.Instance.FactorToRemoveSpaceFromAudioFiles = previous;
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool SetIntercomEffect(int effect)
    {
        var normalized = Math.Clamp(effect, 0, 100);
        var previous = Config.Instance.IntercomEffect;
        Config.Instance.IntercomEffect = normalized;
        if (!TrySaveConfig("intercom effect"))
        {
            Config.Instance.IntercomEffect = previous;
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool TrySelectCodriver(string identity)
    {
        if (IsStageActive)
        {
            return false;
        }

        var codrivers = GetCodriverSnapshot();
        var selectedIndex = CoDriverSelectionIdentity.ResolveIndex(codrivers, identity, -1);
        if (selectedIndex < 0)
        {
            return false;
        }

        var selectedIdentity = CoDriverSelectionIdentity.Create(codrivers[selectedIndex]);
        var previousIndex = Config.Instance.UI_SelectedAudioPackage;
        var previousIdentity = Config.Instance.UI_SelectedAudioPackageIdentity;
        if (!TryApplyCodriverSelection(selectedIndex, selectedIdentity))
        {
            return false;
        }

        try
        {
            Config.Instance.UI_SelectedAudioPackage = selectedIndex;
            Config.Instance.UI_SelectedAudioPackageIdentity = selectedIdentity;
            Config.Instance.SaveUserConfig();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist the selected codriver.");
            Config.Instance.UI_SelectedAudioPackage = previousIndex;
            Config.Instance.UI_SelectedAudioPackageIdentity = previousIdentity;
            RestoreCodriverSelection(previousIndex, previousIdentity);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool TryPlayExample()
    {
        var snapshot = GetSnapshot();
        if (snapshot.IsStageActive || snapshot.IsMuted || snapshot.SelectedCodriver == null)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _isExamplePlaybackPending, 1, 0) != 0)
        {
            return false;
        }

        var requestedAt = Stopwatch.GetTimestamp();
        _ = Task.Run(() =>
        {
            try
            {
                Logger.Debug(
                    "Codriver example playback worker started after {0:F1} ms.",
                    Stopwatch.GetElapsedTime(requestedAt).TotalMilliseconds);
                var current = GetSnapshot();
                if (!current.IsStageActive && !current.IsMuted && current.SelectedCodriver != null)
                {
                    _tool.PlayExample();
                    var requestDelay = Stopwatch.GetElapsedTime(requestedAt).TotalMilliseconds;
                    if (requestDelay >= 250)
                    {
                        Logger.Info(
                            "Codriver example playback was queued {0:F1} ms after the UI request.",
                            requestDelay);
                    }
                    else
                    {
                        Logger.Debug(
                            "Codriver example playback was queued {0:F1} ms after the UI request.",
                            requestDelay);
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to play the codriver example.");
            }
            finally
            {
                Volatile.Write(ref _isExamplePlaybackPending, 0);
            }
        });

        return true;
    }

    public bool SetMuted(bool muted)
    {
        var previous = Config.Instance.UI_Mute;
        if (previous == muted)
        {
            _tool.SetMuted(muted);
            return true;
        }

        Config.Instance.UI_Mute = muted;
        _tool.SetMuted(muted);
        try
        {
            Config.Instance.SaveUserConfig();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist the mute setting.");
            Config.Instance.UI_Mute = previous;
            _tool.SetMuted(previous);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool SetWebDashboardEnabled(bool enabled)
    {
        var previous = Config.Instance.WebDashboardEnabled;
        if (previous == enabled)
        {
            ConfigureDashboard(enabled);
            return true;
        }

        Config.Instance.WebDashboardEnabled = enabled;
        ConfigureDashboard(enabled);
        try
        {
            Config.Instance.SaveUserConfig();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist the Web Dashboard setting.");
            Config.Instance.WebDashboardEnabled = previous;
            ConfigureDashboard(previous);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool ConfigureWebDashboard(int port, int targetFrameRate)
    {
        var normalizedPort = Math.Clamp(port, 1024, 65535);
        var normalizedFrameRate = Math.Clamp(
            targetFrameRate,
            Config.WebDashboardFrameRateMinimum,
            Config.WebDashboardFrameRateMaximum);
        var previousPort = Config.Instance.WebDashboardPort;
        var previousFrameRate = Config.Instance.WebDashboardFrameRate;

        Config.Instance.WebDashboardPort = normalizedPort;
        Config.Instance.WebDashboardFrameRate = normalizedFrameRate;
        ConfigureDashboard(Config.Instance.WebDashboardEnabled);
        if (!TrySaveConfig("Web Dashboard configuration"))
        {
            Config.Instance.WebDashboardPort = previousPort;
            Config.Instance.WebDashboardFrameRate = previousFrameRate;
            ConfigureDashboard(Config.Instance.WebDashboardEnabled);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    private bool IsStageActive => Volatile.Read(ref _isStageActive) != 0;

    private void OnToolInitialized()
    {
        _tool.SetMuted(Config.Instance.UI_Mute);
        RaiseStateChanged();
    }

    private void ConfigureDashboard(bool enabled) =>
        _dashboardController.Configure(
            enabled,
            Config.Instance.WebDashboardPort,
            Config.Instance.WebDashboardFrameRate);

    private void SetStageActive(bool isActive)
    {
        var value = isActive ? 1 : 0;
        if (Interlocked.Exchange(ref _isStageActive, value) != value)
        {
            RaiseStateChanged();
        }
    }

    private IReadOnlyList<CoDriverPackageInfo> GetCodriverSnapshot()
    {
        try
        {
            var snapshot = _tool.CoDriverPackages
                .Select(package => package.Info)
                .Where(package => package != null)
                .ToArray();
            lock (_codriverSnapshotLock)
            {
                _lastKnownCodrivers = snapshot;
            }

            return snapshot;
        }
        catch (InvalidOperationException)
        {
            lock (_codriverSnapshotLock)
            {
                return _lastKnownCodrivers;
            }
        }
    }

    private bool TryApplyCodriverSelection(int selectedIndex, string selectedIdentity)
    {
        if (!string.IsNullOrWhiteSpace(selectedIdentity))
        {
            return _tool.TrySetCodriver(selectedIdentity);
        }

        _tool.SetCodriver(selectedIndex);
        return true;
    }

    private void RestoreCodriverSelection(int previousIndex, string previousIdentity)
    {
        if (!string.IsNullOrWhiteSpace(previousIdentity) &&
            _tool.TrySetCodriver(previousIdentity))
        {
            return;
        }

        _tool.SetCodriver(previousIndex);
    }

    private static int GetSilenceRemovalLevel(float factor)
    {
        if (factor >= 0.000075f)
        {
            return 2;
        }

        return factor >= 0.00003f ? 1 : 0;
    }

    private static bool TrySaveConfig(string setting)
    {
        try
        {
            Config.Instance.SaveUserConfig();
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist the {Setting} setting.", setting);
            return false;
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
