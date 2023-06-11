
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class HomePageVM : ObservableObject {

    private ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool Tool { get; }

    [ObservableProperty]
    private bool _isGameRunning;

    [ObservableProperty]
    private bool _isGameInitialized;

    [ObservableProperty]
    private IList<IGame> _games;

    [ObservableProperty]
    private IList<CoDriverPackage> _codriverPackages;

    [ObservableProperty]
    private IList<string> _outputDevices;



    public HomePageVM(ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool) {
        Tool = _tool;
        _tool.onGameStarted += (game) => IsGameRunning = true;
        _tool.onGameEnded += (game) => IsGameRunning = false;

        _tool.onGameInitialized += (game) => IsGameInitialized = true;
        _tool.onGameInitializeFailed += (game, code) => {
            IsGameInitialized = false;
        };

        _games = new ObservableCollection<IGame>(_tool.Games);
        _codriverPackages = new ObservableCollection<CoDriverPackage>(_tool.CoDriverPackages);
        _outputDevices = new ObservableCollection<string>(_tool.OutputDevices);
    }
}
