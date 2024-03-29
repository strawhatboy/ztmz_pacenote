
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
using ZTMZ.PacenoteTool.WpfGUI.Views;
using ZTMZ.PacenoteTool.Base.UI;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class HomePageVM : ObservableObject {

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
    private IList<GameWithImage> _games = new ObservableCollection<GameWithImage>();

    [ObservableProperty]
    private IList<CoDriverPackageInfo> _codriverPackageInfos = new ObservableCollection<CoDriverPackageInfo>();

    [ObservableProperty]
    private GameWithImage _selectedGame;

    [ObservableProperty]
    private CoDriverPackageInfo _selectedCodriver;

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

    private IDictionary<IGame, IList<object>> _gameConfigSettingsPanes = new Dictionary<IGame, IList<object>>(); 

    private List<Type>? _gameConfigSettingsPaneTypes = null;

    [ObservableProperty]
    private bool _isHudEnabled = Config.Instance.UI_ShowHud;

    partial void OnIsHudEnabledChanged(bool value)
    {
        Config.Instance.UI_ShowHud = value;
        Config.Instance.SaveUserConfig();
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

    public HomePageVM(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool, IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
        var contentDialogService = serviceProvider.GetService(typeof(IContentDialogService)) as IContentDialogService;
        Tool = _tool;
        _tool.onGameStarted += (game) => IsGameRunning = true;
        _tool.onGameEnded += (game) => {
            IsGameRunning = false;
            IsGameInitialized = false;
            IsRacing = false; 
        };

        _tool.onGameInitialized += (game) => {
            IsGameInitialized = true;
            IsGameInitializedFailed = false;
        };
        _tool.onGameInitializeFailed += (game, code) => {
            IsGameInitialized = false;
            IsGameInitializedFailed = code != PrerequisitesCheckResultCode.OK;

            if (code == PrerequisitesCheckResultCode.PORT_NOT_OPEN) {
                // show port not open dialog
                _ = Application.Current.Dispatcher.Invoke(async () =>
                {
                    var result = await contentDialogService?.ShowSimpleDialogAsync(
                        new SimpleContentDialogCreateOptions()
                        {
                            Title = I18NLoader.Instance["dialog.portNotOpen.title"],
                            Content = string.Format(I18NLoader.Instance["dialog.portNotOpen.content"], game.Name, ""),
                            PrimaryButtonText = I18NLoader.Instance["dialog.portNotOpen.btn_ok"],
                            // SecondaryButtonText = "Don't Save",
                            CloseButtonText = I18NLoader.Instance["dialog.portNotOpen.btn_cancel"]
                        }
                    );

                    if (result == ContentDialogResult.Primary) {
                        // force fix
                        game.GamePrerequisiteChecker.ForceFix(game);
                    }
                });
            }
        };
        _tool.onRaceBegin += (game) => { 
            IsRacing = true; 
        };
        _tool.onTrackNameChanged += (game, trackName) => {
            CurrentTrack = trackName;
        };

        _tool.onRaceEnd += (game) => IsRacing = false;

        BindingOperations.EnableCollectionSynchronization(Games, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(CodriverPackageInfos, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(OutputDevices, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(CurrentGameSettings, _collectionLock);

        _tool.onToolInitialized += () => {
            // Application.Current.Dispatcher.Invoke(() => {
                Games.Clear();
                foreach (var game in _tool.Games) {
                    Games.Add(new GameWithImage(game));
                }
                CodriverPackageInfos.Clear();
                foreach (var codriverPackage in _tool.CoDriverPackages) {
                    CodriverPackageInfos.Add(codriverPackage.Info);
                }
                OutputDevices.Clear();
                foreach (var device in _tool.OutputDevices) {
                    OutputDevices.Add(device);
                }
            // });
            _logger.Info("Tool Initialized. in Home Page.");
            var theGame = Games[Config.Instance.UI_SelectedGame];
            var theCodriverPackage = CodriverPackageInfos[Config.Instance.UI_SelectedAudioPackage];
            // Application.Current.Dispatcher.Invoke(() => {      
            SelectedGame = theGame;
            SelectedCodriver = theCodriverPackage;
            SelectedOutputDevice = OutputDevices[Config.Instance.UI_SelectedPlaybackDevice];

            FactorToRemoveSpaceFromAudioFiles = _factorToIndex[Config.Instance.FactorToRemoveSpaceFromAudioFiles];
            PlaybackVolume = (float)(Config.Instance.UI_PlaybackVolume + 1000f) / 20f;  // [-1000, 1000] to [0, 100]
            PlaybackSpeed = Config.Instance.UI_PlaybackSpeed;
            PlaybackAdjustSeconds = (float)Config.Instance.UI_PlaybackAdjustSeconds;
        };
        
        Task.Run(() => {
            // _tool.SetGame(theGame.Game);
            _tool.Init();
            _tool.SetFromConfiguration();
        });
    }

    [RelayCommand]
    private void GameSelectionChanged() {
        var game = SelectedGame;
        Config.Instance.UI_SelectedGame = Games.IndexOf(game);
        Config.Instance.SaveUserConfig();
        Tool.SetGame(game.Game);

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
                    
                    // add separator if not the last one
                    // if (i != settings.Count - 1) {
                    //     CurrentGameSettings.Add(new Separator());
                    // }
                }
            }
        }
       
    }

    [RelayCommand]
    private void CodriverPackageSelectionChanged() {
        var codriverPackage = SelectedCodriver;
        Config.Instance.UI_SelectedAudioPackage = CodriverPackageInfos.IndexOf(codriverPackage);
        Config.Instance.SaveUserConfig();
        Tool.SetCodriver(Config.Instance.UI_SelectedAudioPackage);
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
        // Application.Current.Dispatcher.Invoke(() => {
        Tool.PlayExample();
        // });
    }

    [RelayCommand]
    private void OpenCodriverFolder() {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    System.IO.Path.GetFullPath(SelectedCodriver.Path)));
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
}
