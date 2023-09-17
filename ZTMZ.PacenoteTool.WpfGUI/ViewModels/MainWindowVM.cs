


using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Wpf.Ui.Common;
using Wpf.Ui.Controls.IconElements;
using Wpf.Ui.Controls.Navigation;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WpfGUI.Views;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class MainWindowVM : ObservableObject
{
    private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    bool _isInitialized = false;
    public MainWindowVM(ZTMZPacenoteTool tool, IContentDialogService contentDialogService)
    {
        if (!_isInitialized) {
            _tool = tool;
            _contentDialogService = contentDialogService;
            
            init();

        }
    }
    private readonly IContentDialogService _contentDialogService;

    private StartupDialog _startupDialog;

    [ObservableProperty]
    public string _title = "ZTMZ Next Generation Pacenote Tool";

    
    [ObservableProperty]
    private ObservableCollection<object> _navigationItems = new();

    [ObservableProperty]
    private ObservableCollection<object> _navigationFooter = new();

    private ZTMZPacenoteTool _tool;

    private void init() {
        
        Task t = Task.Run(() => {
            // load i18n
            var jsonPaths = new List<string>{
                    AppLevelVariables.Instance.GetPath(Constants.PATH_LANGUAGE),
                    AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, Constants.PATH_LANGUAGE)),
                    AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_DASHBOARDS, Constants.PATH_LANGUAGE))
                };
                
            NLogManager.init(ToolVersion.TEST);
            _logger.Info("Application started");
            I18NLoader.Instance.Initialize(jsonPaths);
            I18NLoader.Instance.SetCulture(Config.Instance.Language);

            Application.Current.Dispatcher.Invoke(() => {
                NavigationItems = new ObservableCollection<object>
                {
                    new NavigationViewItem()
                    {
                        Content = I18NLoader.Instance["tabs.home"],
                        Icon = new SymbolIcon { Symbol = SymbolRegular.Home20 },
                        TargetPageType = typeof(Views.HomePage)
                    },
                    new NavigationViewItem()
                    {
                        Content = I18NLoader.Instance["tabs.general"],
                        Icon = new SymbolIcon { Symbol = SymbolRegular.Settings20 },
                        TargetPageType = typeof(Views.GeneralPage)
                    },
                    new NavigationViewItem()
                    {
                        Content = I18NLoader.Instance["tabs.voices"],
                        Icon = new SymbolIcon { Symbol = SymbolRegular.PersonVoice20 },
                        // TargetPageType = typeof(Views.Pages.DataPage)
                    },
                    new NavigationViewItem()
                    {
                        Content = I18NLoader.Instance["tabs.playback"],
                        Icon = new SymbolIcon { Symbol = SymbolRegular.Play20 },
                        // TargetPageType = typeof(Views.Pages.DataPage)
                    },
                    new NavigationViewItem()
                    {
                        Content = I18NLoader.Instance["tabs.hud"],
                        Icon = new SymbolIcon { Symbol = SymbolRegular.Gauge20 },
                        // TargetPageType = typeof(Views.Pages.DataPage)
                    },
                    new NavigationViewItem()
                    {
                        Content = I18NLoader.Instance["tabs.game"],
                        Icon = new SymbolIcon { Symbol = SymbolRegular.Games20 },
                        // TargetPageType = typeof(Views.Pages.DataPage)
                    }
                };

                NavigationFooter = new ObservableCollection<object>
                {
                    new NavigationViewItem()
                    {
                        Content = "User",
                        Icon = new SymbolIcon { Symbol = SymbolRegular.PersonCircle20 },
                    },
                    new NavigationViewItem()
                    {
                        Content = "About",
                        Icon = new SymbolIcon { Symbol = SymbolRegular.Info20 },
                        TargetPageType = typeof(Views.SettingsPage)
                    }
                };
            });
            
        });

        _isInitialized = true;
    }

    [RelayCommand]
    private async Task OnShowStartupDialog() {
        // _startupDialog = new StartupDialog(_contentDialogService.GetContentPresenter(), _tool);
        if (!_tool.IsInitialized) {
            // await _startupDialog.ShowAsync();
        }
    }
}

