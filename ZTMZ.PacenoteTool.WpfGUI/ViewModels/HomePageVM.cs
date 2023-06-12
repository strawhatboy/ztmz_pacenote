
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using System.Windows.Data;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class HomePageVM : ObservableObject {

    private ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool Tool { get; }

    [ObservableProperty]
    private bool _isGameRunning;

    [ObservableProperty]
    private bool _isGameInitialized;

    [ObservableProperty]
    private IList<IGame> _games = new ObservableCollection<IGame>();

    [ObservableProperty]
    private IGame _selectedGame;

    [ObservableProperty]
    private IList<CoDriverPackage> _codriverPackages;

    [ObservableProperty]
    private IList<string> _outputDevices;

    private object _collectionLock = new object();

    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public HomePageVM(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool) {
        Tool = _tool;
        _tool.onGameStarted += (game) => IsGameRunning = true;
        _tool.onGameEnded += (game) => IsGameRunning = false;

        _tool.onGameInitialized += (game) => IsGameInitialized = true;
        _tool.onGameInitializeFailed += (game, code) => {
            IsGameInitialized = false;
        };

        BindingOperations.EnableCollectionSynchronization(Games, _collectionLock);

        _tool.onToolInitialized += () => {
            // Application.Current.Dispatcher.Invoke(() => {
                Games.Clear();
                foreach (var game in _tool.Games) {
                    Games.Add(game);
                }
            // });
            _logger.Info("Tool Initialized. in Home Page.");
            _tool.SetGame(Games[Config.Instance.UI_SelectedGame]);
            // Application.Current.Dispatcher.Invoke(() => {      
                SelectedGame = _tool.CurrentGame;
            // });
            // CodriverPackages = new ObservableCollection<CoDriverPackage>(_tool.CoDriverPackages);
            // OutputDevices = new ObservableCollection<string>(_tool.OutputDevices);
        };
    }
}
