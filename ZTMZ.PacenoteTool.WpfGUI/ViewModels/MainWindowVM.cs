


using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Data;

// using Wpf.Ui.Common;
using Wpf.Ui.Controls;
// using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.Views;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class MainWindowVM : ObservableObject
{
    private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    bool _isInitialized = false;

    public event Action OnInitialized;
    public MainWindowVM(ZTMZPacenoteTool tool, IContentDialogService contentDialogService, UpdateService updateService)
    {
        if (!_isInitialized) {
            _tool = tool;
            _contentDialogService = contentDialogService;
            _updateService = updateService;
            
            init();
        }
    }
    private readonly IContentDialogService _contentDialogService;

    private readonly UpdateService _updateService;

    private StartupDialog _startupDialog;

    [ObservableProperty]
    public string _title = "ZTMZ Next Generation Pacenote Tool";

    
    [ObservableProperty]
    private ObservableCollection<object> _navigationItems;
    [ObservableProperty]
    private ObservableCollection<object> _navigationFooter;
    [ObservableProperty]
    private ObservableCollection<object> _trayMenuItems;
    private ZTMZPacenoteTool _tool;

    private object _collectionLock = new object();

    private void init() {
        // No asynchroneous code here, because we need to be sure that the application is fully initialized before we continue.
        // The application may start slowly, but it will be more stable.

        InitNavigationItemsAndFooter();

        // I know it's ugly to set i18n here... but it's the only way to get it working... for now
        I18NHelper.ApplyI18NToApplication();
    
        _isInitialized = true;
        OnInitialized?.Invoke();
    }

    public void InitNavigationItemsAndFooter() {
        
        var items = new List<object>();
        var home = new NavigationViewItem() {
            // Content = I18NLoader.Instance["tabs.home"],
            Icon = new SymbolIcon { Symbol = SymbolRegular.Home20 },
            TargetPageType = typeof(Views.HomePage)
        };
        home.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.home");
        items.Add(home);

        var general = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Settings20 },
            TargetPageType = typeof(Views.GeneralPage)
        };
        general.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.general");
        items.Add(general);

        var voices = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.PersonVoice20 },
            TargetPageType = typeof(Views.VoicePage)
        };
        voices.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.voices");
        items.Add(voices);

        var playback = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Play20 },
            TargetPageType = typeof(Views.PlayPage)
        };
        playback.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.playback");
        items.Add(playback);

        var hud = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Gauge20 },
            TargetPageType = typeof(Views.HudPage)
        };
        hud.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.hud");
        items.Add(hud);

        NavigationItems = new ObservableCollection<object>();
        BindingOperations.EnableCollectionSynchronization(NavigationItems, _collectionLock);
        items.ForEach(a => NavigationItems.Add(a));

        items = new List<object>
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
        NavigationFooter = new ObservableCollection<object>();
        BindingOperations.EnableCollectionSynchronization(NavigationFooter, _collectionLock);
        items.ForEach(a => NavigationFooter.Add(a));

        items = new List<object>
        {
            new MenuItem { Header = "Home", Tag = "tray_home" },
            new MenuItem { Header = "Close", Tag = "tray_close" }
        };

        //TODO: Tray menu items not working now on WPF-UI/Preview-8
        TrayMenuItems = new ObservableCollection<object>();
        BindingOperations.EnableCollectionSynchronization(TrayMenuItems, _collectionLock);
        items.ForEach(a => TrayMenuItems.Add(a));
    }

    [RelayCommand]
    private async Task OnShowStartupDialog() {
        _startupDialog = new StartupDialog(_contentDialogService.GetContentPresenter(), _tool);
        if (!_tool.IsInitialized) {
            await _startupDialog.ShowAsync();
        }
        if (Config.Instance.CheckUpdateWhenStartup) {
            var updateFile = await _updateService.CheckUpdate();
        }
    }
}

