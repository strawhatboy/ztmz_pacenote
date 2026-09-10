
using System;
using System.Collections.Generic;
using System.Net;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.UI.Windows.Game;
using Wpf.Ui.Controls;
using System.Linq;
using System.Windows.Controls;
using ZTMZ.PacenoteTool.WpfGUI.Views;
using ZTMZ.PacenoteTool.UI.Windows;
using System.Diagnostics;
using System.IO;
using System.Threading;
using VRGameOverlay.VROverlayWindow;
using System.CodeDom;
using Wpf.Ui.Extensions;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class HomePageVM : ObservableObject, INavigationAware {

    private ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool Tool { get; }

    [ObservableProperty]
    private bool _isRacing;

    [ObservableProperty]
    private bool _isGameRunning;

    [ObservableProperty]
    private bool _isGameInitialized;

    [ObservableProperty]
    private bool _isGameInitializedFailed;

    [ObservableProperty]
    private string _gameInitializeFailureMessage;

    [ObservableProperty]
    private IList<GameWithImage> _games = new ObservableCollection<GameWithImage>();

    [ObservableProperty]
    private IList<CoDriverPackageInfo> _codriverPackageInfos = new ObservableCollection<CoDriverPackageInfo>();

    [ObservableProperty]
    private GameWithImage _selectedGame;

    private long _gameSelectionGeneration;
    private long _lastQueuedGameSelectionGeneration = -1;
    private readonly SemaphoreSlim _gameSelectionLock = new(1, 1);
    private IGame? _gameSelectionInProgress;
    private readonly object _automaticGameSelectionLock = new();
    private IGame? _pendingAutomaticallyStartedGame;
    private Process? _pendingAutomaticallyStartedProcess;
    private bool _isGameSelectorReady;

    partial void OnSelectedGameChanged(GameWithImage value)
    {
        Interlocked.Increment(ref _gameSelectionGeneration);
    }

    [ObservableProperty]
    private CoDriverPackageInfo? _selectedCodriver;

    [ObservableProperty]
    private IList<AudioOutputDeviceInfo> _outputDevices = new ObservableCollection<AudioOutputDeviceInfo>();

    [ObservableProperty]
    private AudioOutputDeviceInfo? _selectedOutputDevice;

    private object _collectionLock = new object();

    [ObservableProperty]
    private string _infoBarTitle;

    [ObservableProperty]
    private string _infoBarMessage;

    [ObservableProperty]
    private InfoBarSeverity _infoBarSeverity;

    [ObservableProperty]
    private bool _infoBarIsOpen;

    [ObservableProperty]
    private IList<object> _currentGameSettings = new ObservableCollection<object>();

    [ObservableProperty]
    private object? _currentGameResources;

    [ObservableProperty]
    private bool _isGameSettingsUnlocked;

    private IDictionary<IGame, IList<object>> _gameConfigSettingsPanes = new Dictionary<IGame, IList<object>>(); 

    [ObservableProperty]
    private bool _isHudEnabled = Config.Instance.UI_ShowHud;

    partial void OnIsHudEnabledChanged(bool value)
    {
        if (!_isApplyingRuntimeControls)
        {
            _runtimeControls.SetNativeHudVisible(value);
        }
    }

    [ObservableProperty]
    private string _nativeOverlayHomeStatus = string.Empty;

    [ObservableProperty]
    private Brush _nativeOverlayHomeStatusBrush = Brushes.Gray;

    private void RefreshNativeOverlayHomeStatus()
    {
        var status = _gameOverlayManager.CurrentNativeOverlayStatus;
        NativeOverlayHomeStatusBrush = status.State switch
        {
            NativeOverlayState.Active => Brushes.ForestGreen,
            NativeOverlayState.Preview => Brushes.SteelBlue,
            NativeOverlayState.NoDashboard => Brushes.DarkGoldenrod,
            _ => Brushes.Gray
        };
        NativeOverlayHomeStatus = status.State switch
        {
            NativeOverlayState.Disabled => I18NLoader.Instance["settings.tooltip.overlayHudHomeStatusDisabled"],
            NativeOverlayState.NoDashboard => I18NLoader.Instance["settings.tooltip.overlayHudHomeStatusNoDashboard"],
            NativeOverlayState.Ready => I18NLoader.Instance["settings.tooltip.overlayHudHomeStatusReady"],
            NativeOverlayState.Active => I18NLoader.Instance["settings.tooltip.overlayHudHomeStatusActive"],
            NativeOverlayState.Preview => I18NLoader.Instance["settings.tooltip.overlayHudHomeStatusPreview"],
            _ => I18NLoader.Instance["settings.tooltip.overlayHudHomeStatusUnavailable"]
        };
    }

    private void RefreshNativeOverlayHomeStatusOnUiThread()
    {
        if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
        {
            RefreshNativeOverlayHomeStatus();
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            RefreshNativeOverlayHomeStatus();
        });
    }

    #region QuickSettings
    [ObservableProperty]
    private float _playbackVolume = 50.0f;

    [ObservableProperty]
    private string _currentTrack;

    partial void OnPlaybackVolumeChanged(float value)
    {
        Config.Instance.UI_PlaybackVolume = value * 20f - 1000f;
        Tool.SetPlaybackVolume((int)Config.Instance.UI_PlaybackVolume);
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _playbackAdjustSeconds = 0.0f;

    partial void OnPlaybackAdjustSecondsChanged(float value)
    {
        Config.Instance.UI_PlaybackAdjustSeconds = (double)value;
        Tool.SetPlaybackAdjustSeconds(value);
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private float _playbackSpeed = 1.0f;

    partial void OnPlaybackSpeedChanged(float value)
    {
        Config.Instance.UI_PlaybackSpeed = value;
        Tool.SetPlayBackSpeed(value);
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private int _factorToRemoveSpaceFromAudioFiles = 0;

    partial void OnFactorToRemoveSpaceFromAudioFilesChanged(int value)
    {
        Config.Instance.FactorToRemoveSpaceFromAudioFiles = _indexToFactor[value];
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private int _intercomEffect = 0;
    partial void OnIntercomEffectChanged(int value)
    {
        Config.Instance.IntercomEffect = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _autoInitializeGame = Config.Instance.AutoInitializeGame;
    partial void OnAutoInitializeGameChanged(bool value)
    {
        Config.Instance.AutoInitializeGame = value;
        Config.Instance.SaveUserConfig();
    }

    [ObservableProperty]
    private bool _isGameInitializeInProgress = false;

    private static Dictionary<float, int> _factorToIndex = new() {
        { 0.00001f, 0 },
        { 0.00005f, 1 },
        { 0.0001f, 2 },
        { 0.0005f, 3 },
        { 0.001f, 4 },
        { 0.005f, 5 },
        { 0.01f, 6 },
        { 0.05f, 7 },
        { 0.1f, 8 },
        { 0.5f, 9 },
    };
    private static Dictionary<int, float> _indexToFactor = new() {
        { 0, 0.00001f },
        { 1, 0.00005f },
        { 2, 0.0001f },
        { 3, 0.0005f },
        { 4, 0.001f },
        { 5, 0.005f },
        { 6, 0.01f },
        { 7, 0.05f },
        { 8, 0.1f },
        { 9, 0.5f },
    };
    #endregion

    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly IServiceProvider _serviceProvider;

    private readonly IContentDialogService _contentDialogService;

    private readonly DiagnosticsView _diagnosticsView;

    private readonly DispatcherTimer _diagnosticsTimer;

    private readonly SemaphoreSlim _diagnosticsRefreshLock = new(1, 1);

    public DiagnosticsViewVM Diagnostics => _diagnosticsView.ViewModel;

    private GameOverlayManager _gameOverlayManager;

    private VRGameOverlayManager _vrGameOverlayManager;

    private AzureAppInsightsManager _azureAppInsightsManager;

    private UpdateConfigSetter _updateConfigSetter;

    private RuntimeControlsService _runtimeControls;

    private NativeOverlaySessionController _nativeOverlaySession;

    private bool _isApplyingRuntimeControls;
    private readonly object _localBridgePromptLock = new();
    private IGame? _localBridgePromptedGame;

    private void RequestGameOverlayInitialization(IGame game, Process process)
    {
        if (_nativeOverlaySession.EnsureAttached(game, process))
        {
            _logger.Info("Game started in Home Page. Initializing native overlay session.");
            if (OpenVR.IsRuntimeInstalled() && Config.Instance.VrShowOverlay)
            {
                _vrGameOverlayManager.StopLoop();
                _vrGameOverlayManager.StartLoop();
                _logger.Info("VR game overlay initialized.");
            }
        }
    }

    private bool IsCurrentGameSelection(IGame game, long generation)
    {
        return Interlocked.Read(ref _gameSelectionGeneration) == generation
            && SelectedGame?.Game == game;
    }

    private async Task ApplySelectedGameAsync(
        GameWithImage game,
        long generation,
        Process? runningProcess)
    {
        await _gameSelectionLock.WaitAsync();
        try
        {
            if (!IsCurrentGameSelection(game.Game, generation))
            {
                return;
            }

            Volatile.Write(ref _gameSelectionInProgress, game.Game);
            IsGameInitializeInProgress = true;
            var isGameRunning = await Tool.SetGame(game.Game);
            if (IsCurrentGameSelection(game.Game, generation))
            {
                IsGameRunning = isGameRunning;
                if (runningProcess != null)
                {
                    RequestGameOverlayInitialization(game.Game, runningProcess);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(Volatile.Read(ref _gameSelectionInProgress), game.Game))
            {
                Volatile.Write(ref _gameSelectionInProgress, null);
            }

            _gameSelectionLock.Release();
        }
    }

    private void HandleAutomaticallyStartedGame(IGame game, Process process)
    {
        if (!_isGameSelectorReady)
        {
            RememberAutomaticallyStartedGame(game, process);
            return;
        }

        var matchingGame = Games.FirstOrDefault(candidate => candidate.Game == game);
        if (matchingGame == null)
        {
            _logger.Warn("Game {0} started before it was available in the selector.", game.Name);
            RememberAutomaticallyStartedGame(game, process);
            return;
        }

        var selectionAlreadyInProgress = ReferenceEquals(
            Volatile.Read(ref _gameSelectionInProgress),
            game);

        if (selectionAlreadyInProgress && SelectedGame?.Game != game)
        {
            _logger.Debug(
                "Ignoring game-started event for {0} because its selection was superseded.",
                game.Name);
            return;
        }

        var selectionChanged = SelectedGame?.Game != game;
        if (selectionChanged)
        {
            SelectedGame = matchingGame;
        }

        IsGameRunning = true;
        if (selectionAlreadyInProgress)
        {
            RequestGameOverlayInitialization(game, process);
            return;
        }

        if (!selectionChanged)
        {
            // ProcessWatcher (or SetGame's running-process compensation) has already
            // initialized the selected game. Re-entering SetGame here would tear down that
            // reader and its freshly-created overlay while the graphics thread is starting.
            RequestGameOverlayInitialization(game, process);
            return;
        }

        // Changing SelectedGame already advances the selection generation. Reusing that
        // generation coalesces the ComboBox's SelectionChanged command with this automatic
        // selection, so the first SetGame cannot be immediately torn down by a second one.
        QueueGameSelection(force: false, runningProcess: process);
    }

    private void RememberAutomaticallyStartedGame(IGame game, Process process)
    {
        lock (_automaticGameSelectionLock)
        {
            _pendingAutomaticallyStartedGame = game;
            _pendingAutomaticallyStartedProcess = process;
        }

        _logger.Debug("Deferring automatic selection of {0} until the game selector is ready.", game.Name);
    }

    private bool TryTakePendingAutomaticallyStartedGame(out IGame? game, out Process? process)
    {
        lock (_automaticGameSelectionLock)
        {
            game = _pendingAutomaticallyStartedGame;
            process = _pendingAutomaticallyStartedProcess;
            _pendingAutomaticallyStartedGame = null;
            _pendingAutomaticallyStartedProcess = null;
        }

        if (process == null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Discarding a pending automatic game selection because its process is unavailable.");
            return false;
        }
    }

    private void InitializeGameSelection()
    {
        _isGameSelectorReady = true;

        if (Config.Instance.AutoInitializeGame &&
            TryTakePendingAutomaticallyStartedGame(out var automaticallyStartedGame, out var automaticallyStartedProcess) &&
            automaticallyStartedGame != null &&
            automaticallyStartedProcess != null)
        {
            _logger.Info("Applying deferred automatic selection for running game {0}.", automaticallyStartedGame.Name);
            HandleAutomaticallyStartedGame(automaticallyStartedGame, automaticallyStartedProcess);
            return;
        }

        var selectedGameIndex = Config.Instance.UI_SelectedGame;
        if (selectedGameIndex < 0 || selectedGameIndex >= Games.Count)
        {
            _logger.Warn("Saved selected-game index {0} is unavailable; using the first loaded game.", selectedGameIndex);
            selectedGameIndex = 0;
        }

        SelectedGame = Games[selectedGameIndex];
        GameSelectionChanged();
    }

    private void UninitializeGameOverlay()
    {
        _nativeOverlaySession.Detach();
    }

    public HomePageVM(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool, 
        IServiceProvider serviceProvider, 
        GameOverlayManager gameOverlayManager, 
        NativeOverlaySessionController nativeOverlaySession,
        VRGameOverlayManager vRGameOverlayManager,
        AzureAppInsightsManager azureAppInsightsManager,
        UpdateConfigSetter updateConfigSetter,
        IContentDialogService contentDialogService,
        DiagnosticsView diagnosticsView,
        RuntimeControlsService runtimeControls) {
        _gameOverlayManager = gameOverlayManager;
        _nativeOverlaySession = nativeOverlaySession;
        _vrGameOverlayManager = vRGameOverlayManager;
        _serviceProvider = serviceProvider;
        _azureAppInsightsManager = azureAppInsightsManager;
        _updateConfigSetter = updateConfigSetter;
        _contentDialogService = contentDialogService;
        _diagnosticsView = diagnosticsView;
        _runtimeControls = runtimeControls;
        _runtimeControls.StateChanged += OnRuntimeControlsStateChanged;
        _diagnosticsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _diagnosticsTimer.Tick += DiagnosticsTimerTick;
        Tool = _tool;
        GameRuntimeNotifications.LocalBridgeControlsEnabled += game =>
        {
            ResetLocalBridgeInjectionPrompt(game);
            ScheduleLocalBridgeInjectionPrompt(game);
        };
        _gameOverlayManager.OnNativeOverlayStatusChanged += _ =>
        {
            RefreshNativeOverlayHomeStatusOnUiThread();
        };
        RefreshNativeOverlayHomeStatus();
        _tool.onGameStarted += (game, p) => {
            ScheduleLocalBridgeInjectionPrompt(game);
            if (Config.Instance.AutoInitializeGame)
            {
                RunOnUiThread(() => HandleAutomaticallyStartedGame(game, p));
                return;
            }

            var selectedGame = SelectedGame?.Game;
            if (selectedGame != game) {
                _logger.Info("Game started. in Home Page. AutoInitializeGame is false. Do nothing because current selected game is not the started game.");
                return;
            }
            IsGameRunning = true;
            RequestGameOverlayInitialization(game, p);
        };
        _tool.onGameEnded += (game) => {
            ResetLocalBridgeInjectionPrompt(game);
            IsGameRunning = false;
            IsGameInitialized = false;
            IsRacing = false; 

            UninitializeGameOverlay();
        };

        _tool.onGameInitialized += (game) => {
            IsGameInitialized = true;
            IsGameInitializeInProgress = false;
            IsGameInitializedFailed = false;
            game.onCustomMessage += this._gameCustomMessage;
            ScheduleDiagnosticsRefresh(includePrerequisiteCheck: true);
            ScheduleLocalBridgeInjectionPrompt(game);
        };
        _tool.onGameUninitialized += (game) => {
            IsGameInitialized = false;
            IsGameInitializedFailed = false;
            IsRacing = false; 
            game.onCustomMessage -= this._gameCustomMessage;
            ScheduleDiagnosticsRefresh(includePrerequisiteCheck: false);
        };

        _tool.onGameInitializeFailed += (game, code, parameters) => {
            IsGameInitialized = false;
            IsGameInitializeInProgress = false;
            IsGameInitializedFailed = code != PrerequisitesCheckResultCode.OK;
            if (IsGameInitializedFailed) {
                _logger.Warn($"Game {game.Name} initialize failed. Code: {code}");
            }
            var message = "";
            bool showToast = false;

            if (code == PrerequisitesCheckResultCode.PORT_NOT_OPEN) {
                message = string.Format(I18NLoader.Instance["dialog.portNotOpen.content"], parameters[0], parameters[1]);
                // show port not open dialog
                _ = Application.Current.Dispatcher.Invoke(async () =>
                {
                    var result = await new Wpf.Ui.Controls.MessageBox {
                        Title = I18NLoader.Instance["dialog.portNotOpen.title"],
                        Content = message,
                        PrimaryButtonText = I18NLoader.Instance["dialog.portNotOpen.btn_ok"],
                        // SecondaryButtonText = "Don't Save",
                        CloseButtonText = I18NLoader.Instance["dialog.portNotOpen.btn_cancel"]
                    }.ShowDialogAsync();
        

                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary) {
                        // force fix, show toast when fix failed
                        showToast = true;
                        var isFixed = await forceFixGame(game); 
                        if (isFixed) {
                            message = I18NLoader.Instance["settings.restartNeeded"];
                        }
                    }
                });
            } else if (code == PrerequisitesCheckResultCode.CONFIG_FILE_ABNORMAL) {
                message = string.Format(I18NLoader.Instance["exception.configFileAbnormal.msg"], parameters[0], parameters[1]);
                _ = Application.Current.Dispatcher.Invoke(async () =>
                {
                    var result = await new Wpf.Ui.Controls.MessageBox {
                        Title = I18NLoader.Instance["exception.configFileAbnormal.title"],
                        Content = message,
                        PrimaryButtonText = I18NLoader.Instance["exception.configFileAbnormal.btn_ok"],
                        // SecondaryButtonText = "Don't Save",
                        CloseButtonText = I18NLoader.Instance["exception.configFileAbnormal.btn_cancel"]
                    }.ShowDialogAsync();
        

                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary) {
                        // force fix, show toast when fix failed
                        showToast = true;
                        var isFixed = await forceFixGame(game); 
                        if (isFixed) {
                            message = I18NLoader.Instance["settings.restartNeeded"];
                        }
                    }
                });
            } else if (code == PrerequisitesCheckResultCode.PORT_NOT_MATCH && Config.Instance.WarnIfPortMismatch) {
                message = string.Format(I18NLoader.Instance["dialog.portMismatch.content"], 
                    parameters[0], 
                    parameters[1], 
                    parameters[2], 
                    parameters[3]);
                // show port not match dialog
                _ = Application.Current.Dispatcher.Invoke(async () =>
                {
                    var result = await new Wpf.Ui.Controls.MessageBox
                    {
                        Title = I18NLoader.Instance["dialog.portMismatch.title"],
                        Content = message,
                        PrimaryButtonText = I18NLoader.Instance["dialog.portMismatch.btn_FORCE"],
                        SecondaryButtonText = I18NLoader.Instance["dialog.portMismatch.ckbox_show"],
                        CloseButtonText = I18NLoader.Instance["dialog.portMismatch.btn_ok"]
                    }.ShowDialogAsync();

                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary) {
                        // force fix, show toast when fix failed
                        showToast = true;
                        var isFixed = await forceFixGame(game); 
                        if (isFixed) {
                            message = I18NLoader.Instance["settings.restartNeeded"];
                        }
                    } else if (result == Wpf.Ui.Controls.MessageBoxResult.Secondary) {
                        Config.Instance.WarnIfPortMismatch = false;
                        Config.Instance.SaveUserConfig();
                    }
                });
            } else if (code == PrerequisitesCheckResultCode.SETUP_REQUIRED) {
                var setupReason = parameters?.FirstOrDefault()?.ToString();
                message = string.Equals(
                    setupReason,
                    "missing-user-folder",
                    StringComparison.Ordinal)
                    ? I18NLoader.Instance["game.beamng.bridge.setupUserFolderRequired"]
                    : string.Equals(setupReason, "outdated-rally-bridge", StringComparison.Ordinal)
                        ? I18NLoader.Instance["game.beamng.bridge.setupBridgeUpdateRequired"]
                    : I18NLoader.Instance["game.beamng.bridge.setupBridgeRequired"];

                _ = Application.Current.Dispatcher.Invoke(async () =>
                {
                    var result = await new Wpf.Ui.Controls.MessageBox
                    {
                        Title = I18NLoader.Instance["game.beamng.bridge.setupRequiredTitle"],
                        Content = message,
                        PrimaryButtonText = I18NLoader.Instance["game.beamng.bridge.install"],
                        CloseButtonText = I18NLoader.Instance["dialog.common.btn_cancel"]
                    }.ShowDialogAsync();

                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                    {
                        var isFixed = await forceFixGame(game);
                        if (isFixed)
                        {
                            message = I18NLoader.Instance["settings.restartNeeded"];
                        }
                    }
                });
                showToast = true;
            } else if (code == PrerequisitesCheckResultCode.PORT_ALREADY_IN_USE) {
                message = string.Format(I18NLoader.Instance["exception.portAlreadyInUse.msg"], parameters[0]);
                // show port already in use dialog
                _ = Application.Current.Dispatcher.Invoke(async () =>
                {
                    var result = await new Wpf.Ui.Controls.MessageBox
                    {
                        Title = I18NLoader.Instance["exception.portAlreadyInUse.title"],
                        Content = message,
                        CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
                    }.ShowDialogAsync();
                });
                showToast = true;
            } else if (code == PrerequisitesCheckResultCode.PORT_ACCESS_DENIED) {
                var blockedPort = parameters?[0] ?? 0;
                message = string.Format(I18NLoader.Instance["exception.portAccessDenied.msg"], blockedPort);
                _ = Application.Current.Dispatcher.Invoke(async () =>
                {
                    var result = await new Wpf.Ui.Controls.MessageBox
                    {
                        Title = I18NLoader.Instance["exception.portAccessDenied.title"],
                        Content = message,
                        PrimaryButtonText = I18NLoader.Instance["exception.portAccessDenied.btn_useSafePort"],
                        CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
                    }.ShowDialogAsync();

                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                    {
                        var safePort = await configureSafeUdpPort(game);
                        if (safePort.HasValue)
                        {
                            var repairedMessage = string.Format(
                                I18NLoader.Instance["exception.portAccessDenied.fixed"],
                                safePort.Value);
                            GameInitializeFailureMessage = repairedMessage;
                            InfoBarIsOpen = true;
                            InfoBarMessage = repairedMessage;
                            InfoBarSeverity = InfoBarSeverity.Informational;
                        }
                    }
                });
                showToast = true;
            } else if (code == PrerequisitesCheckResultCode.GAME_NOT_INSTALLED) {
                message = I18NLoader.Instance["ui.tooltip.cb_gameNotInstalled"];
                showToast = true;
            } else if (code == PrerequisitesCheckResultCode.CONFIG_FILE_CORRUPTED) {
                message = string.Format(I18NLoader.Instance["exception.configFileCorrupted.msg"], parameters[0], parameters[1]);
                showToast = true;
            }

            // Finally InfoBar closable issue was fixed in WPF-UI 3.0.0
            if (code != PrerequisitesCheckResultCode.OK && !string.IsNullOrEmpty(message) && showToast) {
                GameInitializeFailureMessage = message;
                InfoBarIsOpen = true;
                InfoBarMessage = message;
                InfoBarSeverity = InfoBarSeverity.Warning;
            }
        };
        _tool.onRaceBegin += (game) => { 
            IsRacing = true; 
        };
        _tool.onTrackAndCarChanged += (game, trackName, carName, carClass) => {
            CurrentTrack = trackName;
            var selectedCodriver = _runtimeControls.GetSnapshot().SelectedCodriver;
            _azureAppInsightsManager.TrackEvent("RaceTrackChanged", 
                new Dictionary<string, string>{ 
                    {"Game", game.Name },
                    {"SelectedCodriver", selectedCodriver?.name ?? string.Empty},
                    {"TrackName", trackName },
                    {"CarName", carName }
                });
        };

        _tool.onRaceEnd += (game) => { 
            IsRacing = false;
        };

        _tool.onGameStateChanged += (game, state) => {
            IsRacing = GameStateStageActivity.IsActive(state.NewGameState);
        };

        BindingOperations.EnableCollectionSynchronization(Games, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(CodriverPackageInfos, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(OutputDevices, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(CurrentGameSettings, _collectionLock);

        _tool.onToolInitialized += () => {
            // update new version settings
            updateConfigSetter.SetConfiguration(_tool);

            Games.Clear();
            foreach (var game in _tool.Games) {
                Games.Add(new GameWithImage(game));
            }
            // ApplyRuntimeControls owns audio-device and co-driver list and selection updates,
            // including the StateChanged notification raised during initialization.
            _logger.Info("Tool Initialized. in Home Page.");
            if (Games.Count == 0)
            {
                _logger.Error("No game providers were loaded. Skipping game selection.");
            }
            else
            {
                RunOnUiThread(InitializeGameSelection);
            }

            FactorToRemoveSpaceFromAudioFiles = _factorToIndex[Config.Instance.FactorToRemoveSpaceFromAudioFiles];
            PlaybackVolume = (float)(Config.Instance.UI_PlaybackVolume + 1000f) / 20f;  // [-1000, 1000] to [0, 100]
            PlaybackSpeed = Config.Instance.UI_PlaybackSpeed;
            PlaybackAdjustSeconds = (float)Config.Instance.UI_PlaybackAdjustSeconds;
            IntercomEffect = Config.Instance.IntercomEffect;
        };
        
        _ = Task.Run(async () => {
            // _tool.SetGame(theGame.Game);
            await _tool.InitAsync();
            _tool.SetFromConfiguration();
        });
    }

    public Task OnNavigatedToAsync()
    {
        ApplyRuntimeControls();
        _diagnosticsTimer.Start();
        ScheduleDiagnosticsRefresh(includePrerequisiteCheck: true);
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        _diagnosticsTimer.Stop();
        return Task.CompletedTask;
    }

    private void _gameCustomMessage(int level, string message) {
        _logger.Info($"Game custom message: {message}");
        GameInitializeFailureMessage = message;
        InfoBarIsOpen = true;
        InfoBarMessage = message;
        InfoBarSeverity = (InfoBarSeverity)level;
    }

    private void ResetLocalBridgeInjectionPrompt(IGame game)
    {
        lock (_localBridgePromptLock)
        {
            if (ReferenceEquals(_localBridgePromptedGame, game))
            {
                _localBridgePromptedGame = null;
            }
        }
    }

    private void ScheduleLocalBridgeInjectionPrompt(IGame game)
    {
        if (game is not ILocalGameBridgeControl bridgeControl)
        {
            return;
        }

        var status = bridgeControl.GetLocalBridgeDiagnostics();
        if (!game.IsRunning || !status.IsEnabled || !status.IsAvailable || status.IsInjected)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        lock (_localBridgePromptLock)
        {
            if (ReferenceEquals(_localBridgePromptedGame, game))
            {
                return;
            }

            _localBridgePromptedGame = game;
        }

        _ = dispatcher.InvokeAsync(() => ShowLocalBridgeInjectionPromptAsync(game, bridgeControl)).Task.Unwrap();
    }

    private async Task ShowLocalBridgeInjectionPromptAsync(IGame game, ILocalGameBridgeControl bridgeControl)
    {
        var warningContent = new Grid { Margin = new System.Windows.Thickness(0, 4, 0, 0) };
        warningContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        warningContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var warningIcon = new SymbolIcon
        {
            Symbol = SymbolRegular.Warning24,
            Width = 40,
            Height = 40,
            FontSize = 32,
            Foreground = Brushes.DarkOrange,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new System.Windows.Thickness(0, 0, 16, 0)
        };
        Grid.SetColumn(warningIcon, 0);
        warningContent.Children.Add(warningIcon);

        var warningText = new System.Windows.Controls.TextBlock
        {
            Text = string.Join(
                Environment.NewLine + Environment.NewLine,
                I18NLoader.Instance["game.acr.experimental.launchPromptWarning"],
                I18NLoader.Instance["game.acr.experimental.privilegeWarning"]),
            TextWrapping = System.Windows.TextWrapping.Wrap,
            VerticalAlignment = System.Windows.VerticalAlignment.Top
        };
        Grid.SetColumn(warningText, 1);
        warningContent.Children.Add(warningText);

        var confirmation = await new Wpf.Ui.Controls.MessageBox
        {
            Title = I18NLoader.Instance["game.acr.experimental.launchPromptTitle"],
            Content = warningContent,
            PrimaryButtonText = I18NLoader.Instance["game.acr.experimental.launchPromptInject"],
            CloseButtonText = I18NLoader.Instance["game.acr.experimental.launchPromptNotNow"]
        }.ShowDialogAsync();
        if (confirmation != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var result = await bridgeControl.InjectLocalBridgeAsync();
        if (result.ConfirmAndRetry is { } retry)
        {
            var unknownVersionConfirmation = await new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["game.acr.experimental.launchPromptTitle"],
                Content = result.Detail,
                PrimaryButtonText = I18NLoader.Instance["game.acr.experimental.tryUnverified"],
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_cancel"]
            }.ShowDialogAsync();
            if (unknownVersionConfirmation != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
            result = await retry();
        }
        if (result.Succeeded)
        {
            GameRuntimeNotifications.NotifyLocalBridgeInjected(game);
            return;
        }

        await new Wpf.Ui.Controls.MessageBox
        {
            Title = I18NLoader.Instance["game.acr.experimental.launchPromptTitle"],
            Content = string.Format(I18NLoader.Instance["game.acr.experimental.launchPromptFailure"], result.Detail),
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        }.ShowDialogAsync();
    }

    private async Task<bool> forceFixGame(IGame game) {
        try {
            Tool.ForceFixPrerequisite(game);
            Config.Instance.SaveGameConfig(game);
            RefreshGameSettingsFromConfiguration(game);
            return true;
        } catch (Exception e) {
            _logger.Error(e, "Force fix failed.");
            await new Wpf.Ui.Controls.MessageBox {
                Title = I18NLoader.Instance["exception.forceFixError.title"],
                Content = I18NLoader.Instance["exception.forceFixError.msg"] + e.Message,
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync();
            return false;
        }
    }

    private async Task<int?> configureSafeUdpPort(IGame game)
    {
        if (!game.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var configuration) ||
            configuration is not UdpGameConfig udpConfig ||
            !IPAddress.TryParse(udpConfig.IPAddress, out var ipAddress))
        {
            _logger.Error("Cannot select a safe UDP port because game {0} has no valid UDP configuration.", game.Name);
            return null;
        }

        var previousPort = udpConfig.Port;
        try
        {
            var safePort = UdpPortFinder.FindAvailablePort(ipAddress);
            udpConfig.Port = safePort;

            if (!await forceFixGame(game))
            {
                udpConfig.Port = previousPort;
                return null;
            }

            Config.Instance.SaveGameConfig(game);
            RefreshGameSettingsFromConfiguration(game);
            _logger.Info("Configured game {0} to use safe UDP port {1}.", game.Name, safePort);
            IsGameInitializeInProgress = true;
            IsGameRunning = await Tool.SetGame(game);
            return safePort;
        }
        catch (Exception exception)
        {
            udpConfig.Port = previousPort;
            _logger.Error(exception, "Failed to configure a safe UDP port for game {0}.", game.Name);
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["exception.forceFixError.title"],
                Content = I18NLoader.Instance["exception.forceFixError.msg"] + exception.Message,
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync();
            return null;
        }
    }

    private void RefreshGameSettingsFromConfiguration(IGame game)
    {
        if (!_gameConfigSettingsPanes.TryGetValue(game, out var panes))
        {
            return;
        }

        foreach (var pane in panes.OfType<IGameConfigSettingsPane>())
        {
            pane.RefreshFromConfiguration();
        }
    }

    [RelayCommand]
    private void GameSelectionChanged()
    {
        QueueGameSelection(force: false, runningProcess: null);
    }

    private void QueueGameSelection(bool force, Process? runningProcess)
    {
        var game = SelectedGame;
        if (game == null)
        {
            return;
        }

        var selectionGeneration = force
            ? Interlocked.Increment(ref _gameSelectionGeneration)
            : Interlocked.Read(ref _gameSelectionGeneration);
        if (Interlocked.Exchange(ref _lastQueuedGameSelectionGeneration, selectionGeneration) == selectionGeneration)
        {
            return;
        }

        IsGameSettingsUnlocked = false;
        Config.Instance.UI_SelectedGame = Games.IndexOf(game);
        Config.Instance.SaveUserConfig();
        UninitializeGameOverlay();
        _ = Task.Run(() => ApplySelectedGameAsync(game, selectionGeneration, runningProcess));

        // Update game settings pane
        CurrentGameSettings.Clear();
        var settings = game.Game.GameConfigurations;

        if (_gameConfigSettingsPanes.ContainsKey(game.Game)) {
            foreach (var pane in _gameConfigSettingsPanes[game.Game]) {
                CurrentGameSettings.Add(pane);
            }
        } else {
            _gameConfigSettingsPanes[game.Game] = new List<object>();
            for (var i = 0; i < settings.Count; i++) {
                var setting = settings.ElementAt(i);
                IGameSettingsProvider? provider = BuiltInGameSettingsProviders.Find(setting.Value);
                if (provider is null && game.Game is IGameWithIntegrationProviders integrationGame)
                {
                    provider = integrationGame.IntegrationProviders.FirstOrDefault(candidate =>
                        candidate.ConfigurationType.IsInstanceOfType(setting.Value));
                }
                var instance = provider is null
                    ? null
                    : new GameIntegrationConfigSettingsPane(setting.Value, provider);

                if (instance != null) {
                    instance.InitializeWithGame(game.Game);
                    CurrentGameSettings.Add(instance);
                    _gameConfigSettingsPanes[game.Game].Add(instance);
                    instance.RestartNeeded += () => {
                        _logger.Info("Game config changed. Restart needed.");
                        InfoBarIsOpen = true;
                        InfoBarMessage = I18NLoader.Instance["settings.restartNeeded"];
                        InfoBarSeverity = InfoBarSeverity.Warning;
                    };
                }
            }
        }
        var gameResources = new StackPanel();
        if (game.Game is IGameWithModCatalog modGame)
        {
            gameResources.Children.Add(new GameModCatalogPane(game.Game, modGame));
        }
        if (game.Game is IGameWithResourceLinks { ResourceLinks.Count: > 0 } resourceGame)
        {
            var links = new GameResourceLinksPane(resourceGame.ResourceLinks);
            if (gameResources.Children.Count > 0)
                links.Margin = new System.Windows.Thickness(0, 12, 0, 0);
            gameResources.Children.Add(links);
        }
        CurrentGameResources = gameResources.Children.Count > 0 ? gameResources : null;

        ScheduleDiagnosticsRefresh(includePrerequisiteCheck: true);
    }

    [RelayCommand]
    private async Task UnlockGameSettings()
    {
        if (IsGameSettingsUnlocked)
        {
            return;
        }

        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = I18NLoader.Instance["settings.gameSettingsUnlockDialogTitle"],
            Content = I18NLoader.Instance["settings.gameSettingsUnlockDialogMessage"],
            PrimaryButtonText = I18NLoader.Instance["settings.gameSettingsUnlockConfirm"],
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_cancel"]
        }.ShowDialogAsync();

        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            IsGameSettingsUnlocked = true;
        }
    }

    [RelayCommand]
    private void CodriverPackageSelectionChanged() {
        if (_isApplyingRuntimeControls || SelectedCodriver == null) return;
        var codriverPackage = SelectedCodriver;
        if (!_runtimeControls.TrySelectCodriver(CoDriverSelectionIdentity.Create(codriverPackage)))
        {
            ApplyRuntimeControls();
        }
    }

    [RelayCommand]
    private void OutputDeviceSelectionChanged() {
        var outputDevice = SelectedOutputDevice;
        if (_isApplyingRuntimeControls || outputDevice == null)
        {
            return;
        }

        if (!_runtimeControls.TrySelectOutputDevice(outputDevice.Id))
        {
            ApplyRuntimeControls();
        }
    }

    [RelayCommand]
    private void PlayExample() {
        _runtimeControls.TryPlayExample();
    }

    [RelayCommand]
    private void OpenCodriverFolder() {
        if (SelectedCodriver == null || string.IsNullOrWhiteSpace(SelectedCodriver.Path))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    System.IO.Path.GetFullPath(SelectedCodriver.Path)));
    }

    private void OnRuntimeControlsStateChanged(object? sender, EventArgs eventArgs)
    {
        RunOnUiThread(ApplyRuntimeControls);
    }

    private void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.BeginInvoke(action);
    }

    private void ApplyRuntimeControls()
    {
        ApplyRuntimeControls(_runtimeControls.GetSnapshot());
    }

    private void ApplyRuntimeControls(RuntimeControlsSnapshot snapshot)
    {
        _isApplyingRuntimeControls = true;
        try
        {
            IsHudEnabled = snapshot.IsNativeHudVisible;
            if (!OutputDevices.Select(device => device.Id).SequenceEqual(
                    snapshot.OutputDevices.Select(device => device.Id)))
            {
                OutputDevices.Clear();
                foreach (var device in snapshot.OutputDevices)
                {
                    OutputDevices.Add(device);
                }
            }

            SelectedOutputDevice = snapshot.SelectedOutputDevice == null
                ? null
                : OutputDevices.FirstOrDefault(device => string.Equals(
                    device.Id,
                    snapshot.SelectedOutputDevice.Id,
                    StringComparison.OrdinalIgnoreCase));
            // A reinstalled package can retain its identity but have new metadata.
            // Compare instances so refreshes replace stale entries without rebuilding
            // the list on unrelated runtime state changes.
            if (!CodriverPackageInfos.SequenceEqual(snapshot.Codrivers, ReferenceEqualityComparer.Instance))
            {
                CodriverPackageInfos.Clear();
                foreach (var package in snapshot.Codrivers)
                {
                    CodriverPackageInfos.Add(package);
                }
            }

            if (snapshot.SelectedCodriver == null)
            {
                SelectedCodriver = null;
                return;
            }

            var selectedIdentity = CoDriverSelectionIdentity.Create(snapshot.SelectedCodriver);
            SelectedCodriver = CodriverPackageInfos.FirstOrDefault(package =>
                                  string.Equals(
                                      CoDriverSelectionIdentity.Create(package),
                                      selectedIdentity,
                                      StringComparison.OrdinalIgnoreCase))
                              ?? snapshot.SelectedCodriver;
        }
        finally
        {
            _isApplyingRuntimeControls = false;
        }
    }

    [RelayCommand]
    private void MoreCodriverSettings() {
        var _navigationWindow = (_serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow)!;
        _navigationWindow.Navigate(typeof(VoicePage));
    }

    [RelayCommand]
    private void MoreHudSettings() {
        var _navigationWindow = (_serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow)!;
        _navigationWindow.Navigate(typeof(HudPage));
    }

    [RelayCommand]
    private void MorePlaySettings() {
        var _navigationWindow = (_serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow)!;
        _navigationWindow.Navigate(typeof(PlayPage));
    }

    [RelayCommand]
    private async Task OpenCurrentTrackPacenotes()
    {
        var game = Tool.CurrentGame;
        if (!IsRacing || game is null || string.IsNullOrWhiteSpace(CurrentTrack))
        {
            return;
        }

        var editor = _serviceProvider.GetService(typeof(ScriptEditorPage)) as ScriptEditorPage;
        var navigationWindow = _serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow;
        if (editor is null || navigationWindow is null)
        {
            return;
        }

        var scriptPath = Tool.ProfileManager.CurrentScriptPath ?? string.Empty;
        try
        {
            var opened = File.Exists(scriptPath) &&
                         string.Equals(Path.GetExtension(scriptPath), ".pacenote", StringComparison.OrdinalIgnoreCase)
                ? await editor.OpenScriptAsync(scriptPath)
                : await OpenMemoryBackedPacenotesAsync(editor, game, scriptPath);
            if (opened)
            {
                navigationWindow.Navigate(typeof(ScriptEditorPage));
            }
        }
        catch (Exception exception)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(exception, "Could not open current track pacenotes in the editor.");
            InfoBarMessage = exception.Message;
            InfoBarSeverity = InfoBarSeverity.Error;
            InfoBarIsOpen = true;
        }
    }

    private async Task<bool> OpenMemoryBackedPacenotesAsync(ScriptEditorPage editor, IGame game, string scriptPath)
    {
        var script = await Task.Run(() =>
            game.GamePacenoteReader.ReadPacenoteRecord(Tool.ProfileManager.CurrentProfile, game, CurrentTrack));
        if (script is null || script.PacenoteRecords.Count == 0)
        {
            return false;
        }

        return await editor.OpenReadOnlyScriptAsync(
            script.ToString(),
            $"{game.Name}: {CurrentTrack}",
            scriptPath);
    }

    [RelayCommand]
    private Task RefreshDiagnostics()
    {
        return RefreshDiagnosticsAsync(includePrerequisiteCheck: true);
    }

    [RelayCommand]
    private async Task OpenDiagnosticsDialog()
    {
        await RefreshDiagnosticsAsync(includePrerequisiteCheck: true);
        await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
        {
            Title = I18NLoader.Instance["diagnostics.title"],
            Content = _diagnosticsView,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        });
    }

    private async void DiagnosticsTimerTick(object sender, EventArgs e)
    {
        await RefreshDiagnosticsAsync(includePrerequisiteCheck: false);
    }

    private void ScheduleDiagnosticsRefresh(bool includePrerequisiteCheck)
    {
        if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
        {
            _ = RefreshDiagnosticsAsync(includePrerequisiteCheck);
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _ = RefreshDiagnosticsAsync(includePrerequisiteCheck);
        });
    }

    private async Task RefreshDiagnosticsAsync(bool includePrerequisiteCheck)
    {
        if (includePrerequisiteCheck)
        {
            await _diagnosticsRefreshLock.WaitAsync();
        }
        else if (!await _diagnosticsRefreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (includePrerequisiteCheck)
            {
                await Diagnostics.RefreshAsync();
            }
            else
            {
                await Diagnostics.RefreshRuntimeAsync();
            }
        }
        finally
        {
            _diagnosticsRefreshLock.Release();
        }
    }
}
