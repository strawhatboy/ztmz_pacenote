#nullable enable

using System;
using System.Collections.Generic;
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
    bool IsStageActive,
    bool IsMuted,
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

        return new SharedRuntimeControlsSnapshot(
            codrivers,
            selectedIndex >= 0 ? codrivers[selectedIndex] : null,
            IsStageActive,
            Config.Instance.UI_Mute,
            Config.Instance.WebDashboardEnabled,
            _dashboardController.Status);
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

        _ = Task.Run(() =>
        {
            try
            {
                var current = GetSnapshot();
                if (!current.IsStageActive && !current.IsMuted && current.SelectedCodriver != null)
                {
                    _tool.PlayExample();
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

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
