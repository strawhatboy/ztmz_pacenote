
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;
using System.Threading.Tasks;
using ZTMZ.PacenoteTool.Base.UI.Game;
using Wpf.Ui.Controls;

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
    private GameWithImage _selectedGame;

    [ObservableProperty]
    private IList<CoDriverPackage> _codriverPackages;

    [ObservableProperty]
    private IList<string> _outputDevices;

    private object _collectionLock = new object();

    [ObservableProperty]
    private string _infoBarTitle;

    [ObservableProperty]
    private string _infoBarMessage;

    [ObservableProperty]
    private InfoBarSeverity _infoBarSeverity;

    [ObservableProperty]
    private bool _infoBarIsOpen;

    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public HomePageVM(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool) {
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
            IsGameInitializedFailed = true;
        };
        _tool.onRaceBegin += (game) => IsRacing = true;
        _tool.onRaceEnd += (game) => IsRacing = false;

        BindingOperations.EnableCollectionSynchronization(Games, _collectionLock);

        _tool.onToolInitialized += () => {
            // Application.Current.Dispatcher.Invoke(() => {
                Games.Clear();
                foreach (var game in _tool.Games) {
                    Games.Add(new GameWithImage(game));
                }
            // });
            _logger.Info("Tool Initialized. in Home Page.");
            var theGame = Games[Config.Instance.UI_SelectedGame];
            // Application.Current.Dispatcher.Invoke(() => {      
            SelectedGame = theGame;
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
    }
}
