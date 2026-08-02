
using System;
using System.Collections.Generic;
using System.Net;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.UI.Game;
using Wpf.Ui.Controls;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using ZTMZ.PacenoteTool.WpfGUI.Views;
using ZTMZ.PacenoteTool.Base.UI;
using System.Diagnostics;
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

    [ObservableProperty]
    private CoDriverPackageInfo? _selectedCodriver;

    [ObservableProperty]
    private IList<string> _outputDevices = new ObservableCollection<string>();

    [ObservableProperty]
    private string _selectedOutputDevice;

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
    private bool _isGameSettingsUnlocked;

    private IDictionary<IGame, IList<object>> _gameConfigSettingsPanes = new Dictionary<IGame, IList<object>>(); 

    private List<Type>? _gameConfigSettingsPaneTypes = null;

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

    private bool _isApplyingRuntimeControls;

    public HomePageVM(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool, 
        IServiceProvider serviceProvider, 
        GameOverlayManager gameOverlayManager, 
        VRGameOverlayManager vRGameOverlayManager,
        AzureAppInsightsManager azureAppInsightsManager,
        UpdateConfigSetter updateConfigSetter,
        IContentDialogService contentDialogService,
        DiagnosticsView diagnosticsView,
        RuntimeControlsService runtimeControls) {
        _gameOverlayManager = gameOverlayManager;
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
        _gameOverlayManager.OnNativeOverlayStatusChanged += _ =>
        {
            RefreshNativeOverlayHomeStatusOnUiThread();
        };
        RefreshNativeOverlayHomeStatus();
        _tool.onGameStarted += (game, p) => {
            if (!Config.Instance.AutoInitializeGame && SelectedGame.Game != game) {
                _logger.Info("Game started. in Home Page. AutoInitializeGame is false. Do nothing because current selected game is not the started game.");
                return;
            }
            if (Config.Instance.AutoInitializeGame && SelectedGame.Game != game) {
                SelectedGame = Games.Where(g => g.Game == game).FirstOrDefault();
                return;
            }
            IsGameRunning = true;
            _gameOverlayManager.SetStageActive(false);
            // GameOverlay
            _logger.Info("Game started. in Home Page. Initializing GameOverlay.");
            Task.Run(() => {
                _logger.Info("Wait for 5 seconds..In case the machine is too slow to launch the game");
                Thread.Sleep(5000);
                _logger.Info("Initializing GameOverlay in game window...");
                _gameOverlayManager.UninitializeOverlay();
                _gameOverlayManager.InitializeOverlay(p);

                _logger.Info("Try to initialize VRGameOverlay...");
                if (OpenVR.IsRuntimeInstalled() && Config.Instance.VrShowOverlay) {
                    _vrGameOverlayManager.StopLoop();
                    _vrGameOverlayManager.StartLoop();
                    _logger.Info("Vr Game overlay initliazed.");
                }
            });
        };
        _tool.onGameEnded += (game) => {
            IsGameRunning = false;
            IsGameInitialized = false;
            IsRacing = false; 

            _gameOverlayManager.SetStageActive(false);
            _gameOverlayManager.UninitializeOverlay();
        };

        _tool.onGameInitialized += (game) => {
            IsGameInitialized = true;
            IsGameInitializeInProgress = false;
            IsGameInitializedFailed = false;
            game.onCustomMessage += this._gameCustomMessage;
            ScheduleDiagnosticsRefresh(includePrerequisiteCheck: true);
        };
        _tool.onGameUninitialized += (game) => {
            IsGameInitialized = false;
            IsGameInitializedFailed = false;
            IsRacing = false; 
            _gameOverlayManager.SetStageActive(false);
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
            _gameOverlayManager.SetStageActive(true);
            _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplayComparison = ReplayComparisonSnapshot.Unavailable;
        };
        _tool.onRaceBegined += (game) => {
            _gameOverlayManager.DashboardScriptArguments.GameContext.ScriptAuthor = _tool.CurrentScriptAuthor;
        };
        _tool.onTrackAndCarChanged += (game, trackName, carName, carClass) => {
            CurrentTrack = trackName;
            _gameOverlayManager.DashboardScriptArguments.GameContext.TrackName = trackName;
            _gameOverlayManager.DashboardScriptArguments.GameContext.CarName = carName;
            _gameOverlayManager.DashboardScriptArguments.GameContext.CarClass = carClass;
            var selectedCodriver = _runtimeControls.GetSnapshot().SelectedCodriver;
            _gameOverlayManager.DashboardScriptArguments.GameContext.AudioPackage = selectedCodriver?.name ?? string.Empty;
            _azureAppInsightsManager.TrackEvent("RaceTrackChanged", 
                new Dictionary<string, string>{ 
                    {"Game", game.Name },
                    {"SelectedCodriver", selectedCodriver?.name ?? string.Empty},
                    {"TrackName", trackName },
                    {"CarName", carName }
                });
        };

        _tool.onLocalReplayLoaded += res => {
            _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplayValid = res;
            _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplay = _tool.BestLocalReplay;
            _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplayDetailsPerCheckpoints = _tool.BestLocalReplayDetailsPerCheckpoints;
            _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplayDetailsPerTimes = _tool.BestLocalReplayDetailsPerTimes;
            _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplayDetailsPerTimesDict = _tool.BestLocalReplayDetailsPerTimes
                .GroupBy(x => x.time)
                .ToDictionary(x => x.Key, x => x.First().distance);
            _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplayFinishTime = _tool.BestLocalReplay?.finish_time ?? 0;
            _gameOverlayManager.DashboardScriptArguments.GameContext.GetDelta = _tool.GetDelta;
            _gameOverlayManager.DashboardScriptArguments.GameContext.GetDeltaByDistanceAndTime = _tool.GetDeltaByDistanceAndTime;
            _gameOverlayManager.DashboardScriptArguments.GameContext.GetDeltaByCheckpoint = _tool.GetDeltaByCheckpoint;
            _gameOverlayManager.DashboardScriptArguments.GameContext.GetTimeByDistance = _tool.GetTimeByDistance;
            _gameOverlayManager.DashboardScriptArguments.GameContext.GetBestTimeByDistance = _tool.GetBestTimeByDistance;
            _gameOverlayManager.DashboardScriptArguments.GameContext.GetBestDistanceByTime = _tool.GetBestDistanceByTime;
            _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplayComparison = ReplayComparisonSnapshot.Unavailable;
        };

        _tool.onNewGameData += (game, data) => {
            if (_isRacing) {
                // update game data.
                _gameOverlayManager.DashboardScriptArguments.GameData = data;
                _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplayComparison =
                    _tool.GetReplayComparison(data);
            }
        };

        _tool.onRaceEnd += (game) => { 
            IsRacing = false;
            _gameOverlayManager.SetStageActive(false);
            _gameOverlayManager.DashboardScriptArguments.GameContext.LocalReplayComparison = ReplayComparisonSnapshot.Unavailable;
        };

        _tool.onGameStateChanged += (game, state) => {
            // set Gamestate to dashboard context.
            _gameOverlayManager.DashboardScriptArguments.GameContext.LastGameState = (int)state.LastGameState;
            _gameOverlayManager.DashboardScriptArguments.GameContext.GameState = (int)state.NewGameState;
            _gameOverlayManager.DashboardScriptArguments.GameContext.GameStateChangedParameters = state.Parameters;
            if (state.NewGameState is GameState.RaceEnd or GameState.Unknown)
            {
                _gameOverlayManager.SetStageActive(false);
            }
        };

        BindingOperations.EnableCollectionSynchronization(Games, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(CodriverPackageInfos, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(OutputDevices, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(CurrentGameSettings, _collectionLock);

        _tool.onCodriversRefreshed += () => {
            RunOnUiThread(loadCodrivers);
        };

        _tool.onToolInitialized += () => {
            // update new version settings
            updateConfigSetter.SetConfiguration(_tool);

            // Application.Current.Dispatcher.Invoke(() => {
                Games.Clear();
                foreach (var game in _tool.Games) {
                    Games.Add(new GameWithImage(game));
                }
                OutputDevices.Clear();
                foreach (var device in _tool.OutputDevices) {
                    OutputDevices.Add(device);
                }
            // });
            _logger.Info("Tool Initialized. in Home Page.");
            if (Games.Count == 0)
            {
                _logger.Error("No game providers were loaded. Skipping game selection.");
            }
            else
            {
                var selectedGameIndex = Config.Instance.UI_SelectedGame;
                if (selectedGameIndex < 0 || selectedGameIndex >= Games.Count)
                {
                    _logger.Warn("Saved selected-game index {0} is unavailable; using the first loaded game.", selectedGameIndex);
                    selectedGameIndex = 0;
                }

                SelectedGame = Games[selectedGameIndex];
            }

            if (OutputDevices.Count == 0)
            {
                _logger.Warn("No audio output devices were loaded.");
            }
            else
            {
                SelectedOutputDevice = OutputDevices[(Config.Instance.UI_SelectedPlaybackDevice < 0 || Config.Instance.UI_SelectedPlaybackDevice >= OutputDevices.Count) ? 0 : Config.Instance.UI_SelectedPlaybackDevice];
            }
            loadCodrivers();
            FactorToRemoveSpaceFromAudioFiles = _factorToIndex[Config.Instance.FactorToRemoveSpaceFromAudioFiles];
            PlaybackVolume = (float)(Config.Instance.UI_PlaybackVolume + 1000f) / 20f;  // [-1000, 1000] to [0, 100]
            PlaybackSpeed = Config.Instance.UI_PlaybackSpeed;
            PlaybackAdjustSeconds = (float)Config.Instance.UI_PlaybackAdjustSeconds;
            IntercomEffect = Config.Instance.IntercomEffect;
        };
        
        Task.Run(() => {
            // _tool.SetGame(theGame.Game);
            _tool.Init();
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

    private async Task<bool> forceFixGame(IGame game) {
        try {
            game.GamePrerequisiteChecker.ForceFix(game);
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

    private void loadCodrivers() {
        CodriverPackageInfos.Clear();
        foreach (var codriverPackage in Tool.CoDriverPackages) {
            CodriverPackageInfos.Add(codriverPackage.Info);
        }
        ApplyRuntimeControls();
    }

    [RelayCommand]
    private async void GameSelectionChanged() {
        var game = SelectedGame;
        IsGameSettingsUnlocked = false;
        Config.Instance.UI_SelectedGame = Games.IndexOf(game);
        Config.Instance.SaveUserConfig();
        _ = Task.Run(async () => {
            IsGameInitializeInProgress = true;
            IsGameRunning = await Tool.SetGame(game.Game);
        });

        // Update game settings pane
        CurrentGameSettings.Clear();
        var settings = game.Game.GameConfigurations;

        if (_gameConfigSettingsPaneTypes == null) {
            _gameConfigSettingsPaneTypes = new List<Type>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                _gameConfigSettingsPaneTypes.AddRange(asm.GetTypes().Where(t => typeof(IGameConfigSettingsPane).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract));
            }
        }
            
        if (_gameConfigSettingsPanes.ContainsKey(game.Game)) {
            foreach (var pane in _gameConfigSettingsPanes[game.Game]) {
                CurrentGameSettings.Add(pane);
            }
        } else {
            _gameConfigSettingsPanes[game.Game] = new List<object>();
            for (var i = 0; i < settings.Count; i++) {
                var setting = settings.ElementAt(i);
                var pane = _gameConfigSettingsPaneTypes.Where(t => t.GetCustomAttribute<GameConfigPaneAttribute>()?.PaneType == setting.Value.GetType()).FirstOrDefault();
                if (pane != null) {
                    var instance = Activator.CreateInstance(pane, setting.Value) as IGameConfigSettingsPane;
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

        ScheduleDiagnosticsRefresh(includePrerequisiteCheck: true);

        // TODO: UGLY CODE
        // for EAWRC, localization part
        ShowWRCLocalization = game.Game.Name == "EA SPORTS™ WRC";
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
        Config.Instance.UI_SelectedPlaybackDevice = OutputDevices.IndexOf(outputDevice);
        Config.Instance.SaveUserConfig();
        Tool.SetOutputDevice(Config.Instance.UI_SelectedPlaybackDevice);
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
        var snapshot = _runtimeControls.GetSnapshot();
        _isApplyingRuntimeControls = true;
        try
        {
            IsHudEnabled = snapshot.IsNativeHudVisible;
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

    // TODO: UGLY CODE
    // for EAWRC, localization part
    #region EAWRC
    [ObservableProperty]
    private bool _showWRCLocalization = false;

    [RelayCommand]
    private void OpenChinesePacenote() {
        // open url https://gitee.com/ztmz/ea_wrc_chinese_codrivers/releases in default browser
        Process.Start(new ProcessStartInfo("https://gitee.com/ztmz/ea_wrc_chinese_codrivers/releases") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenChineseLocalization() {
        // open url https://gitee.com/ztmz/ea_wrc_chinese_translation/releases in default browser
        Process.Start(new ProcessStartInfo("https://gitee.com/ztmz/ea_wrc_chinese_translation/releases") { UseShellExecute = true });
    }
    #endregion
}
