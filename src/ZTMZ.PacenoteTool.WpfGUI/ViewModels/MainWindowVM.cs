


using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

// using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

// using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.UI.Windows;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WpfGUI.Models;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.Views;
using ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public enum TrayWindowAction
{
    ShowMainUi,
    ShowUpdate,
    ManageCodrivers,
    Exit
}

public partial class MainWindowVM : ObservableObject
{
    private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    bool _isInitialized = false;

    public event Action OnInitialized;
    internal event Action<TrayWindowAction>? TrayWindowActionRequested;
    internal event Action<string>? UpdateAvailableInTray;

    public MainWindowVM(
        ZTMZPacenoteTool tool,
        IContentDialogService contentDialogService,
        UpdateService updateService,
        AzureAppInsightsManager azureAppInsightsManager,
        RuntimeControlsService runtimeControls)
    {
        _runtimeControls = runtimeControls;
        _runtimeControls.StateChanged += OnRuntimeControlsStateChanged;

        if (!_isInitialized) {
            _tool = tool;
            _contentDialogService = contentDialogService;
            _updateService = updateService;
            _azureAppInsightsManager = azureAppInsightsManager;
            
            init();
        }
    }
    private readonly IContentDialogService _contentDialogService;

    private readonly UpdateService _updateService;

    private StartupDialog _startupDialog;
    private UpdateFile? _pendingUpdate;
    private bool _isShowingPendingUpdate;

    private AzureAppInsightsManager _azureAppInsightsManager;

    [ObservableProperty]
    public string _title = "ZTMZ Next Generation Pacenote Tool - " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? String.Empty;

    
    [ObservableProperty]
    private ObservableCollection<object> _navigationItems;
    [ObservableProperty]
    private ObservableCollection<object> _navigationFooter;
    [ObservableProperty]
    private ObservableCollection<object> _trayMenuItems;
    private ZTMZPacenoteTool _tool;
    private RuntimeControlsService _runtimeControls;

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

        var scriptEditor = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Code20 },
            TargetPageType = typeof(Views.ScriptEditorPage)
        };
        scriptEditor.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.scriptEditor");
        items.Add(scriptEditor);

        var hud = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Gauge20 },
            TargetPageType = typeof(Views.HudPage)
        };
        hud.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.hud");
        items.Add(hud);

        var vr = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.HeadsetVr20 },
            TargetPageType = typeof(Views.VrPage)
        };
        vr.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.vr");
        items.Add(vr);

        var replay = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Replay20 },
            TargetPageType = typeof(Views.ReplayPage)
        };
        replay.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.replay");
        items.Add(replay);

        NavigationItems = new ObservableCollection<object>();
        BindingOperations.EnableCollectionSynchronization(NavigationItems, _collectionLock);
        items.ForEach(a => NavigationItems.Add(a));

        items = new List<object>();
        var user = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.PersonCircle20 },
            TargetPageType = typeof(Views.UserPage)
        };
        user.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.user");
        items.Add(user);

        var about = new NavigationViewItem() {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Info20 },
            TargetPageType = typeof(Views.SettingsPage)
        };
        about.SetResourceReference(NavigationViewItem.ContentProperty, "tabs.about");
        items.Add(about);


        NavigationFooter = new ObservableCollection<object>();
        BindingOperations.EnableCollectionSynchronization(NavigationFooter, _collectionLock);
        items.ForEach(a => NavigationFooter.Add(a));

        TrayMenuItems = new ObservableCollection<object>();
        BindingOperations.EnableCollectionSynchronization(TrayMenuItems, _collectionLock);
    }

    public void RefreshTrayMenu()
    {
        if (TrayMenuItems == null)
        {
            return;
        }

        try
        {
            RefreshTrayMenuCore();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to refresh the tray menu.");
        }
    }

    private void RefreshTrayMenuCore()
    {
        var snapshot = _runtimeControls.GetSnapshot();
        var state = snapshot.ToTrayMenuState();
        var menuItems = new List<object>();

        var selectedDisplayName = state.SelectedCodriver?.Package.DisplayText ?? I18NLoader.Instance["tray.noCodrivers"];
        var codriverMenu = new MenuItem
        {
            Header = string.Format(I18NLoader.Instance["tray.codriver"], selectedDisplayName),
            Icon = new SymbolIcon { Symbol = SymbolRegular.PersonVoice20 }
        };

        if (state.Codrivers.Count == 0)
        {
            codriverMenu.Items.Add(new MenuItem
            {
                Header = I18NLoader.Instance["tray.noCodrivers"],
                IsEnabled = false
            });
        }
        else
        {
            foreach (var option in state.Codrivers)
            {
                var packageItem = new MenuItem
                {
                    Header = option.Package.DisplayText,
                    ToolTip = state.CanSelectCodriver
                        ? option.Package.description
                        : I18NLoader.Instance["tray.unavailableDuringStage"],
                    IsCheckable = true,
                    IsChecked = option.IsSelected,
                    IsEnabled = state.CanSelectCodriver,
                    Command = SelectTrayCodriverCommand,
                    CommandParameter = option.Identity
                };
                System.Windows.Controls.ToolTipService.SetShowOnDisabled(packageItem, true);
                codriverMenu.Items.Add(packageItem);
            }
        }

        codriverMenu.Items.Add(new System.Windows.Controls.Separator());
        codriverMenu.Items.Add(new MenuItem
        {
            Header = I18NLoader.Instance["tray.manageCodrivers"],
            Icon = new SymbolIcon { Symbol = SymbolRegular.Settings20 },
            Command = RequestTrayWindowActionCommand,
            CommandParameter = TrayWindowAction.ManageCodrivers
        });
        menuItems.Add(codriverMenu);

        var playExampleTooltip = state.CanPlayExample
            ? I18NLoader.Instance["ui.tooltip.btn_playExample"]
            : snapshot.IsStageActive
                ? I18NLoader.Instance["tray.unavailableDuringStage"]
                : state.IsMuted
                    ? I18NLoader.Instance["tray.unmuteToPlayExample"]
                    : I18NLoader.Instance["tray.noCodrivers"];
        var playExampleItem = new MenuItem
        {
            Header = I18NLoader.Instance["ui.btn_playExample"],
            Icon = new SymbolIcon { Symbol = SymbolRegular.Play20 },
            ToolTip = playExampleTooltip,
            IsEnabled = state.CanPlayExample,
            Command = PlayTrayExampleCommand
        };
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(playExampleItem, true);
        menuItems.Add(playExampleItem);

        menuItems.Add(new MenuItem
        {
            Header = I18NLoader.Instance["tray.muteAudio"],
            Icon = new SymbolIcon { Symbol = state.IsMuted ? SymbolRegular.SpeakerMute20 : SymbolRegular.Speaker120 },
            IsCheckable = true,
            IsChecked = state.IsMuted,
            Command = ToggleTrayMuteCommand
        });

        menuItems.Add(new System.Windows.Controls.Separator());
        if (_pendingUpdate != null)
        {
            menuItems.Add(new MenuItem
            {
                Header = string.Format("{0} ({1})", I18NLoader.Instance["dialog.newUpdate.title"], _pendingUpdate.version),
                Icon = new SymbolIcon { Symbol = SymbolRegular.Info20 },
                Command = RequestTrayWindowActionCommand,
                CommandParameter = TrayWindowAction.ShowUpdate
            });
        }
        menuItems.Add(new MenuItem
        {
            Header = I18NLoader.Instance["tray.overlayHud"],
            Icon = new SymbolIcon { Symbol = SymbolRegular.Gauge20 },
            IsCheckable = true,
            IsChecked = state.IsNativeHudVisible,
            Command = ToggleTrayHudCommand
        });
        menuItems.Add(new MenuItem
        {
            Header = I18NLoader.Instance["tray.webDashboard"],
            Icon = new SymbolIcon { Symbol = SymbolRegular.Globe20 },
            ToolTip = GetWebDashboardTooltip(snapshot),
            IsCheckable = true,
            IsChecked = state.IsWebDashboardEnabled,
            Command = ToggleTrayWebDashboardCommand
        });

        menuItems.Add(new System.Windows.Controls.Separator());
        menuItems.Add(new MenuItem
        {
            Header = I18NLoader.Instance["ui.showMainUI"],
            Icon = new SymbolIcon { Symbol = SymbolRegular.Home20 },
            Command = RequestTrayWindowActionCommand,
            CommandParameter = TrayWindowAction.ShowMainUi
        });
        menuItems.Add(new MenuItem
        {
            Header = I18NLoader.Instance["ui.closeToExit"],
            Icon = new SymbolIcon { Symbol = SymbolRegular.ClosedCaption16 },
            Command = RequestTrayWindowActionCommand,
            CommandParameter = TrayWindowAction.Exit
        });

        TrayMenuItems.Clear();
        menuItems.ForEach(TrayMenuItems.Add);
    }

    [RelayCommand]
    private void SelectTrayCodriver(string identity)
    {
        _runtimeControls.TrySelectCodriver(identity);
    }

    [RelayCommand]
    private void PlayTrayExample()
    {
        _runtimeControls.TryPlayExample();
    }

    [RelayCommand]
    private void ToggleTrayMute()
    {
        _runtimeControls.SetMuted(!Config.Instance.UI_Mute);
    }

    [RelayCommand]
    private void ToggleTrayHud()
    {
        _runtimeControls.SetNativeHudVisible(!Config.Instance.UI_ShowHud);
    }

    [RelayCommand]
    private void ToggleTrayWebDashboard()
    {
        _runtimeControls.SetWebDashboardEnabled(!Config.Instance.WebDashboardEnabled);
    }

    [RelayCommand]
    private void RequestTrayWindowAction(TrayWindowAction action)
    {
        TrayWindowActionRequested?.Invoke(action);
    }

    private void OnRuntimeControlsStateChanged(object? sender, EventArgs eventArgs)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(RefreshTrayMenu);
    }

    private static string GetWebDashboardTooltip(RuntimeControlsSnapshot snapshot)
    {
        if (!snapshot.IsWebDashboardEnabled)
        {
            return I18NLoader.Instance["tray.webDashboardStopped"];
        }

        if (snapshot.WebDashboardStatus.IsRunning)
        {
            return I18NLoader.Instance["tray.webDashboardRunning"];
        }

        return string.IsNullOrWhiteSpace(snapshot.WebDashboardStatus.ErrorMessage)
            ? I18NLoader.Instance["tray.webDashboardStarting"]
            : string.Format(
                I18NLoader.Instance["tray.webDashboardError"],
                snapshot.WebDashboardStatus.ErrorMessage);
    }

    [RelayCommand]
    private async Task OnShowStartupDialog() {
        var shouldCheckForUpdate = Config.Instance.CheckUpdateWhenStartup || ToolUtils.GetToolVersion() != ToolVersion.STANDARD;

        // A tray-only start should not be interrupted by startup dialogs. It still checks for
        // updates so the tray icon can notify the user and offer the update from its menu.
        if (Config.Instance.StartMinimizedToTray) {
            if (shouldCheckForUpdate)
            {
                await CheckForUpdateInTrayAsync();
            }

            return;
        }

        _startupDialog = new StartupDialog(_contentDialogService.GetDialogHostEx()!, _tool);
        if (!_tool.IsInitialized) {
            await _startupDialog.ShowAsync();
        }
        if (shouldCheckForUpdate) {
            // force update if it is not standard version
            try {
                var updateFile = await _updateService.CheckUpdate();
                _azureAppInsightsManager.TrackEvent("CheckUpdate", new Dictionary<string, string>{ {"Result", updateFile == null ? "NoUpdate" : "UpdateAvailable" }});
            } catch (Exception ex) {
                _azureAppInsightsManager.TrackException(ex, new Dictionary<string, string>{ {"CheckUpdate", "Failed" }});
                _logger.Error(ex, "CheckUpdate failed");
            }
        }
    }

    internal async Task ShowPendingUpdateDialogAsync()
    {
        var pendingUpdate = _pendingUpdate;
        if (pendingUpdate == null || _isShowingPendingUpdate)
        {
            return;
        }

        _isShowingPendingUpdate = true;
        try
        {
            var accepted = await _updateService.ShowUpdateDialogAsync(pendingUpdate);
            if (accepted || string.Equals(Config.Instance.SkippedVersion, pendingUpdate.version, StringComparison.OrdinalIgnoreCase))
            {
                _pendingUpdate = null;
                RefreshTrayMenu();
            }
        }
        catch (Exception ex)
        {
            _azureAppInsightsManager.TrackException(ex, new Dictionary<string, string> { { "CheckUpdate", "ShowPendingDialogFailed" } });
            _logger.Error(ex, "Failed to show the pending update dialog.");
        }
        finally
        {
            _isShowingPendingUpdate = false;
        }
    }

    private async Task CheckForUpdateInTrayAsync()
    {
        try
        {
            var updateFile = await _updateService.CheckForUpdate();
            _azureAppInsightsManager.TrackEvent("CheckUpdate", new Dictionary<string, string>
            {
                { "Result", updateFile == null ? "NoUpdate" : "UpdateAvailable" }
            });

            if (updateFile == null)
            {
                return;
            }

            _pendingUpdate = updateFile;
            RefreshTrayMenu();
            UpdateAvailableInTray?.Invoke(updateFile.version);
        }
        catch (Exception ex)
        {
            _azureAppInsightsManager.TrackException(ex, new Dictionary<string, string> { { "CheckUpdate", "Failed" } });
            _logger.Error(ex, "CheckUpdate failed");
        }
    }

    [RelayCommand]
    private async Task RaiseIssue() {
        if (Config.Instance.Language == "zh-cn") {
            // navigate to the issue page on gitee
            Process.Start("explorer.exe", "https://gitee.com/ztmz/ztmz_pacenote/issues");
        } else {
            // navigate to the issue page on github
            Process.Start("explorer.exe", "https://github.com/strawhatboy/ztmz_pacenote/issues");
        }
    }

    [RelayCommand]
    private async Task ShowHelp() {
        Process.Start("explorer.exe", Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Assets\ztmz_ngptool_3.0_help.png"));
    }

    [RelayCommand]
    private async Task BuyMeACoffee() {
        Process.Start("explorer.exe", "https://gitee.com/ztmz/ztmz_pacenote/tree/master/buy_me_a_coffee");
    }

}
