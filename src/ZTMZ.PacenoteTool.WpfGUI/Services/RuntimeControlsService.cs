using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

public sealed record RuntimeControlsSnapshot(
    IReadOnlyList<CoDriverPackageInfo> Codrivers,
    CoDriverPackageInfo? SelectedCodriver,
    bool IsStageActive,
    bool IsMuted,
    bool IsNativeHudVisible,
    bool IsWebDashboardEnabled,
    WebDashboardServerStatus WebDashboardStatus)
{
    public TrayQuickControlMenuState ToTrayMenuState()
    {
        return TrayQuickControlMenuBuilder.Build(
            Codrivers,
            SelectedCodriver == null ? string.Empty : CoDriverSelectionIdentity.Create(SelectedCodriver),
            IsStageActive,
            IsMuted,
            IsNativeHudVisible,
            IsWebDashboardEnabled);
    }
}

public sealed class RuntimeControlsService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ZTMZPacenoteTool _tool;
    private readonly GameOverlayManager _gameOverlayManager;
    private readonly AzureAppInsightsManager _azureAppInsightsManager;
    private readonly object _codriverSnapshotLock = new();
    private IReadOnlyList<CoDriverPackageInfo> _lastKnownCodrivers = Array.Empty<CoDriverPackageInfo>();
    private int _isStageActive;
    private int _isExamplePlaybackPending;

    public RuntimeControlsService(
        ZTMZPacenoteTool tool,
        GameOverlayManager gameOverlayManager,
        AzureAppInsightsManager azureAppInsightsManager)
    {
        _tool = tool;
        _gameOverlayManager = gameOverlayManager;
        _azureAppInsightsManager = azureAppInsightsManager;

        _tool.onToolInitialized += OnToolInitialized;
        _tool.onCodriversRefreshed += RaiseStateChanged;
        _tool.onRaceBegin += _ => SetStageActive(true);
        _tool.onRaceEnd += _ => SetStageActive(false);
        _tool.onGameEnded += _ => SetStageActive(false);
        _tool.onGameUninitialized += _ => SetStageActive(false);
        _tool.onGameStateChanged += (_, state) =>
        {
            if (state.NewGameState is GameState.RaceEnd or GameState.Unknown)
            {
                SetStageActive(false);
            }
        };
        _gameOverlayManager.OnWebDashboardStatusChanged += _ => RaiseStateChanged();
    }

    public event EventHandler? StateChanged;

    public RuntimeControlsSnapshot GetSnapshot()
    {
        var codrivers = GetCodriverSnapshot();
        var selectedIndex = CoDriverSelectionIdentity.ResolveIndex(
            codrivers,
            Config.Instance.UI_SelectedAudioPackageIdentity,
            Config.Instance.UI_SelectedAudioPackage);

        var selectedCodriver = selectedIndex >= 0 ? codrivers[selectedIndex] : null;
        return new RuntimeControlsSnapshot(
            codrivers,
            selectedCodriver,
            IsStageActive,
            Config.Instance.UI_Mute,
            Config.Instance.UI_ShowHud,
            Config.Instance.WebDashboardEnabled,
            _gameOverlayManager.WebDashboardStatus);
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

        _azureAppInsightsManager.TrackEvent(
            "CodriverPackageSelectionChanged",
            new Dictionary<string, string> { { "SelectedCodriver", codrivers[selectedIndex].name ?? string.Empty } });
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
                var currentSnapshot = GetSnapshot();
                if (!currentSnapshot.IsStageActive && !currentSnapshot.IsMuted && currentSnapshot.SelectedCodriver != null)
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
        var previousValue = Config.Instance.UI_Mute;
        if (previousValue == muted)
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
            Config.Instance.UI_Mute = previousValue;
            _tool.SetMuted(previousValue);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    public bool SetNativeHudVisible(bool visible)
    {
        var previousValue = Config.Instance.UI_ShowHud;
        if (previousValue == visible)
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
            Config.Instance.UI_ShowHud = previousValue;
            _gameOverlayManager.RefreshNativeOverlayVisibility();
            return false;
        }

        _gameOverlayManager.RefreshNativeOverlayVisibility();
        RaiseStateChanged();
        return true;
    }

    public bool SetWebDashboardEnabled(bool enabled)
    {
        var previousValue = Config.Instance.WebDashboardEnabled;
        if (previousValue == enabled)
        {
            _gameOverlayManager.ConfigureWebDashboard(
                enabled,
                Config.Instance.WebDashboardPort,
                Config.Instance.WebDashboardFrameRate);
            return true;
        }

        Config.Instance.WebDashboardEnabled = enabled;
        _gameOverlayManager.ConfigureWebDashboard(
            enabled,
            Config.Instance.WebDashboardPort,
            Config.Instance.WebDashboardFrameRate);

        try
        {
            Config.Instance.SaveUserConfig();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist the Web Dashboard setting.");
            Config.Instance.WebDashboardEnabled = previousValue;
            _gameOverlayManager.ConfigureWebDashboard(
                previousValue,
                Config.Instance.WebDashboardPort,
                Config.Instance.WebDashboardFrameRate);
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

    private void SetStageActive(bool isActive)
    {
        if (Interlocked.Exchange(ref _isStageActive, isActive ? 1 : 0) == (isActive ? 1 : 0))
        {
            return;
        }

        RaiseStateChanged();
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
        if (!string.IsNullOrWhiteSpace(previousIdentity) && _tool.TrySetCodriver(previousIdentity))
        {
            return;
        }

        _tool.SetCodriver(previousIndex);
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
