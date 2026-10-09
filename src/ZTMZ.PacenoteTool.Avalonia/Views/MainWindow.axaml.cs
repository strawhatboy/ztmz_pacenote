using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Avalonia.ViewModels;
using ZTMZ.PacenoteTool.Base;
#if WINDOWS
using ZTMZ.PacenoteTool.Platform.Windows;
#else
using ZTMZ.PacenoteTool.Platform.Linux;
#endif

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public sealed partial class MainWindow : FAAppWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly ListBox _navigationList;
    private readonly Grid _pageHost;
    private readonly FABreadcrumbBar _navigationBreadcrumb;
    private readonly Dictionary<string, Control> _pages;
    private readonly GeneralSettingsViewModel _generalSettingsViewModel;
    private readonly PlaySettingsViewModel _playSettingsViewModel;
    private readonly ISystemNotificationService _systemNotificationService;
    private MainWindowViewModel? _viewModel;
    private bool _exitRequested;
    private bool _closeFlowActive;
    private AvaloniaTrayService? _trayService;

    internal MainWindow(MainWindowViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        var windowIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(windowIconPath))
        {
            ((Window)this).Icon = new WindowIcon(windowIconPath);
        }
        SplashScreen = new ZtmzApplicationSplashScreen(viewModel);
        var customTitleBar = this.FindControl<Grid>("CustomTitleBar") ??
            throw new InvalidOperationException("Custom title bar was not created.");
        var titleBarActions = this.FindControl<StackPanel>("TitleBarActions") ??
            throw new InvalidOperationException("Title bar actions were not created.");
        var usesWindowsTitleBar = OperatingSystem.IsWindows();
        TitleBar.ExtendsContentIntoTitleBar = usesWindowsTitleBar;
        customTitleBar.IsVisible = true;
        titleBarActions.Margin = usesWindowsTitleBar
            ? new Thickness(0, 0, 142, 0)
            : new Thickness(0, 0, 12, 0);

        _navigationList = this.FindControl<ListBox>("NavigationList") ??
            throw new InvalidOperationException("Navigation list was not created.");
        _pageHost = this.FindControl<Grid>("PageHost") ??
            throw new InvalidOperationException("Page host was not created.");
        _navigationBreadcrumb = this.FindControl<FABreadcrumbBar>("NavigationBreadcrumb") ??
            throw new InvalidOperationException("Navigation breadcrumb was not created.");
        _navigationBreadcrumb.ItemClicked += OnNavigationBreadcrumbItemClicked;
        var homeNavigationItem = this.FindControl<ListBoxItem>("HomeNavigationItem") ??
            throw new InvalidOperationException("Home navigation item was not created.");

        _generalSettingsViewModel = new GeneralSettingsViewModel();
        _generalSettingsViewModel.NotificationRequested += OnSystemNotificationRequested;
        _generalSettingsViewModel.LogDirectoryViewRequested += OnLogDirectoryViewRequested;
        _playSettingsViewModel = new PlaySettingsViewModel();
        _pages = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = new HomeView(),
            ["general"] = new GeneralView { DataContext = _generalSettingsViewModel },
            ["games"] = new GamesView(),
            ["codriver"] = new CoDriverView(),
            ["dashboard"] = new DashboardView(),
            ["play"] = new PlayView { DataContext = _playSettingsViewModel },
            ["about"] = new AboutView()
        };

        _navigationList.SelectionChanged += OnNavigationSelectionChanged;
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        AvaloniaLocalization.CultureChanged += OnCultureChanged;
        DataContext = viewModel;
        InitializeTray();
#if WINDOWS
        _systemNotificationService = new WindowsSystemNotificationService(
            () => _trayService?.GetPlatformImplementation());
#else
        _systemNotificationService = new LinuxSystemNotificationService();
#endif
        foreach (var page in _pages.Values)
        {
            page.IsVisible = false;
            _pageHost.Children.Add(page);
        }
        _navigationList.SelectedItem = homeNavigationItem;
        ShowPage("home");
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            IsInteractiveTitleBarElement(eventArgs.Source))
        {
            return;
        }

        if (eventArgs.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            eventArgs.Handled = true;
            return;
        }

        BeginMoveDrag(eventArgs);
        eventArgs.Handled = true;
    }

    private static bool IsInteractiveTitleBarElement(object? source)
    {
        for (var visual = source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is Button)
            {
                return true;
            }
        }

        return false;
    }

    private void OnNavigationSelectionChanged(
        object? sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_navigationList.SelectedItem is ListBoxItem item &&
            item.Tag is string pageKey)
        {
            ShowPage(pageKey);
            if (string.Equals(pageKey, "general", StringComparison.OrdinalIgnoreCase))
            {
                _generalSettingsViewModel.RefreshFromConfig();
            }
        }
    }

    private void OnAboutNavigationClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        ShowPage("about");
        _navigationList.SelectedItem = null;
        eventArgs.Handled = true;
    }

    private void ShowPage(string pageKey)
    {
        // NativeWebView must stay under the same native parent. Hide existing pages before
        // adding the lazily created editor, then attach it once in its visible state.
        foreach (var child in _pageHost.Children)
        {
            child.IsVisible = false;
        }

        if (!_pages.TryGetValue(pageKey, out var page))
        {
            if (!string.Equals(pageKey, "scriptEditor", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            page = new ScriptEditorView();
            _pages.Add(pageKey, page);
            _pageHost.Children.Add(page);
        }

        page.IsVisible = true;
        _viewModel?.SetActiveNavigationPage(pageKey);
    }

    private void OnNavigationBreadcrumbItemClicked(
        FABreadcrumbBar sender,
        FABreadcrumbBarItemClickedEventArgs eventArgs)
    {
        if (eventArgs.Index != 0 || _viewModel == null ||
            !string.Equals(_viewModel.ActiveNavigationPage, "codriver", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_viewModel.IsVoiceSettingsOpen)
            _viewModel.CloseVoiceSettingsCommand.Execute(null);
        else if (_viewModel.IsVoicePackageDetailsOpen)
            _viewModel.CloseVoicePackageDetailsCommand.Execute(null);
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        DetachViewModel();
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel != null)
        {
            _viewModel.NavigationRequested += OnNavigationRequested;
            _viewModel.SystemNotificationRequested += OnSystemNotificationRequested;
            _viewModel.FolderOpenRequested += OnFolderOpenRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.CoDrivers.CollectionChanged += OnCoDriversCollectionChanged;
            UpdateTrayMenu();
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.NavigationRequested -= OnNavigationRequested;
            _viewModel.SystemNotificationRequested -= OnSystemNotificationRequested;
            _viewModel.FolderOpenRequested -= OnFolderOpenRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.CoDrivers.CollectionChanged -= OnCoDriversCollectionChanged;
            _viewModel = null;
        }
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        LocalBridgeDialogs.NotifyWindowReady();
#if WINDOWS
        if (Config.Instance.StartMinimizedToTray && _trayService != null)
        {
            Hide();
        }
#endif
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_exitRequested)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_closeFlowActive)
        {
            return;
        }

        _closeFlowActive = true;
        try
        {
            if (Config.Instance.ShowClosePrompt && !await ConfirmCloseAsync())
            {
                return;
            }

#if WINDOWS
            if (Config.Instance.CloseWindowToMinimize && _trayService != null)
            {
                Hide();
                return;
            }
#endif

            if (_pages.TryGetValue("scriptEditor", out var scriptEditorPage) &&
                scriptEditorPage is ScriptEditorView scriptEditor &&
                !await scriptEditor.ConfirmDiscardChangesAsync())
            {
                return;
            }

            _exitRequested = true;
            Close();
        }
        finally
        {
            _closeFlowActive = false;
        }
    }

    private async Task<bool> ConfirmCloseAsync()
    {
        var closeToExit = new RadioButton
        {
            Content = AvaloniaLocalization.Get("ui.closeToExit"),
            IsChecked = !Config.Instance.CloseWindowToMinimize,
            GroupName = "CloseBehavior"
        };
        var closeToTray = new RadioButton
        {
            Content = AvaloniaLocalization.Get("ui.closeToMinimize"),
            IsChecked = Config.Instance.CloseWindowToMinimize,
            GroupName = "CloseBehavior",
            IsVisible = OperatingSystem.IsWindows()
        };
        var dontShowAgain = new CheckBox
        {
            Content = AvaloniaLocalization.Get("dialog.closePrompt.dontShowAgain")
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(closeToExit);
        content.Children.Add(closeToTray);
        content.Children.Add(dontShowAgain);

        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = AvaloniaLocalization.Get("ui.closeBehavior"),
            Content = content,
            PrimaryButtonText = AvaloniaLocalization.Get("dialog.closePrompt.ok"),
            CloseButtonText = AvaloniaLocalization.Get("dialog.closePrompt.cancel"),
            DefaultButton = FAContentDialogButton.Primary
        });
        var result = await dialog.ShowAsync(this);
        if (result != FAContentDialogResult.Primary)
        {
            return false;
        }

        Config.Instance.CloseWindowToMinimize = closeToTray.IsVisible && closeToTray.IsChecked == true;
        Config.Instance.ShowClosePrompt = dontShowAgain.IsChecked != true;
        Config.Instance.SaveUserConfig();
        return true;
    }

    internal void RequestDataFolderMigrationShutdown()
    {
        _exitRequested = true;
        Close();
    }

    private async void OnClosed(object? sender, EventArgs eventArgs)
    {
        AvaloniaLocalization.CultureChanged -= OnCultureChanged;
        _generalSettingsViewModel.NotificationRequested -= OnSystemNotificationRequested;
        _generalSettingsViewModel.LogDirectoryViewRequested -= OnLogDirectoryViewRequested;
        _generalSettingsViewModel.Dispose();
        _playSettingsViewModel.Dispose();
        DetachViewModel();
        foreach (var page in _pages.Values.OfType<IAsyncDisposable>())
        {
            await page.DisposeAsync();
        }
        if (_trayService != null)
        {
            _trayService.ShowRequested -= OnTrayShowRequested;
            _trayService.Dispose();
        }
        if (_systemNotificationService is IDisposable disposableNotificationService)
        {
            disposableNotificationService.Dispose();
        }
    }

    private void OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        UpdateTrayMenu();
    }

    private void OnSystemNotificationRequested(string title, string message) =>
        _ = ShowSystemNotificationAsync(title, message);

    private async void OnFolderOpenRequested(string directory)
    {
        if (await DesktopFolderLauncher.TryOpenAsync(directory))
        {
            return;
        }

        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = AvaloniaLocalization.Get("settings.tooltip.viewLogs"),
            Content = new SelectableTextBlock
            {
                Text = string.Format(AvaloniaLocalization.Get("settings.tooltip.folderOpenFailed"), directory),
                TextWrapping = TextWrapping.Wrap,
                Width = 560
            },
            CloseButtonText = AvaloniaLocalization.Get("dialog.common.btn_ok")
        });
        await dialog.ShowAsync(this);
    }

    private async void OnLogDirectoryViewRequested(string directory)
    {
        var content = await Task.Run(() => ReadRecentLogs(directory));
        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = AvaloniaLocalization.Get("settings.tooltip.viewLogs"),
            Content = new ScrollViewer
            {
                Width = 800,
                Height = 560,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new SelectableTextBlock
                {
                    Text = content,
                    TextWrapping = TextWrapping.Wrap
                }
            },
            CloseButtonText = AvaloniaLocalization.Get("dialog.common.btn_ok")
        });
        await dialog.ShowAsync(this);
    }

    private static string ReadRecentLogs(string directory)
    {
        try
        {
            var files = new DirectoryInfo(directory)
                .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(3)
                .ToArray();
            if (files.Length == 0)
            {
                return directory + Environment.NewLine + Environment.NewLine + "No log files have been written yet.";
            }

            var sections = new List<string> { directory };
            foreach (var file in files)
            {
                sections.Add(string.Empty);
                sections.Add($"===== {file.Name} =====");
                sections.Add(File.ReadAllText(file.FullName));
            }

            return string.Join(Environment.NewLine, sections);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not load logs for the in-app viewer.");
            return directory + Environment.NewLine + Environment.NewLine + exception.Message;
        }
    }

    private async Task ShowSystemNotificationAsync(string title, string message)
    {
        try
        {
            if (!await _systemNotificationService.TryShowAsync(new SystemNotification(title, message)))
            {
                Logger.Debug("The operating system did not accept the requested notification.");
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to dispatch an operating-system notification.");
        }
    }

    private void OnTrayShowRequested(object? sender, EventArgs eventArgs)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void RequestTrayExit()
    {
        _exitRequested = true;
        Close();
    }

    private void InitializeTray()
    {
        var trayIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app_tray.ico");
        if (!File.Exists(trayIconPath))
        {
            return;
        }

        _trayService = new AvaloniaTrayService(trayIconPath);
        _trayService.ShowRequested += OnTrayShowRequested;
        UpdateTrayMenu();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainWindowViewModel.SelectedCoDriver) or
            nameof(MainWindowViewModel.IsReady) or
            nameof(MainWindowViewModel.IsStageActive) or
            nameof(MainWindowViewModel.IsMuted) or
            nameof(MainWindowViewModel.IsWebDashboardEnabled) or
            nameof(MainWindowViewModel.DashboardUrl) or
            nameof(MainWindowViewModel.DashboardStatus))
        {
            UpdateTrayMenu();
        }
    }

    private void OnCoDriversCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) =>
        UpdateTrayMenu();

    private void UpdateTrayMenu()
    {
        if (_trayService == null)
        {
            return;
        }

        var viewModel = _viewModel;
        if (viewModel == null)
        {
            return;
        }

        var selectedName = viewModel.SelectedCoDriver?.DisplayName ??
            AvaloniaLocalization.Get("tray.noCodrivers");
        var codriverItems = new List<TrayMenuEntry>();
        if (viewModel.CoDrivers.Count == 0)
        {
            codriverItems.Add(new TrayMenuEntry(
                AvaloniaLocalization.Get("tray.noCodrivers"),
                IsEnabled: false));
        }
        else
        {
            foreach (var codriver in viewModel.CoDrivers)
            {
                var option = codriver;
                codriverItems.Add(new TrayMenuEntry(
                    option.DisplayName,
                    IsEnabled: !viewModel.IsStageActive,
                    IsChecked: string.Equals(
                        option.Identity,
                        viewModel.SelectedCoDriver?.Identity,
                        StringComparison.Ordinal),
                    ToggleType: TrayMenuToggleType.Radio,
                    ToolTip: viewModel.IsStageActive
                        ? AvaloniaLocalization.Get("tray.unavailableDuringStage")
                        : option.Description,
                    Activate: () => viewModel.SelectedCoDriver = option));
            }
        }

        codriverItems.Add(TrayMenuEntry.Separator());
        codriverItems.Add(new TrayMenuEntry(
            AvaloniaLocalization.Get("tray.manageCodrivers"),
            Activate: () => viewModel.MoreCoDriverSettingsCommand.Execute(null)));

        var canPlayExample = viewModel.PlayExampleCommand.CanExecute(null);
        var playExampleToolTip = canPlayExample
            ? AvaloniaLocalization.Get("ui.tooltip.btn_playExample")
            : viewModel.IsStageActive
                ? AvaloniaLocalization.Get("tray.unavailableDuringStage")
                : viewModel.IsMuted
                    ? AvaloniaLocalization.Get("tray.unmuteToPlayExample")
                    : AvaloniaLocalization.Get("tray.noCodrivers");
        var dashboardToolTip = !viewModel.IsWebDashboardEnabled
            ? AvaloniaLocalization.Get("tray.webDashboardStopped")
            : !string.IsNullOrWhiteSpace(viewModel.DashboardUrl)
                ? AvaloniaLocalization.Get("tray.webDashboardRunning")
                : viewModel.DashboardStatus;

        var entries = new List<TrayMenuEntry>
        {
            new(
                AvaloniaLocalization.Format("tray.codriver", selectedName),
                Children: codriverItems),
            new(
                AvaloniaLocalization.Get("ui.btn_playExample"),
                IsEnabled: canPlayExample,
                ToolTip: playExampleToolTip,
                Activate: () => viewModel.PlayExampleCommand.Execute(null)),
            new(
                AvaloniaLocalization.Get("tray.muteAudio"),
                IsChecked: viewModel.IsMuted,
                ToggleType: TrayMenuToggleType.CheckBox,
                Activate: () => viewModel.IsMuted = !viewModel.IsMuted),
            TrayMenuEntry.Separator(),
            new(
                AvaloniaLocalization.Get("tray.webDashboard"),
                IsChecked: viewModel.IsWebDashboardEnabled,
                ToggleType: TrayMenuToggleType.CheckBox,
                ToolTip: dashboardToolTip,
                Activate: () => viewModel.IsWebDashboardEnabled = !viewModel.IsWebDashboardEnabled),
            TrayMenuEntry.Separator(),
            new(
                AvaloniaLocalization.Get("ui.showMainUI"),
                Activate: OnTrayShow),
            new(
                AvaloniaLocalization.Get("ui.closeToExit"),
                Activate: RequestTrayExit)
        };

        _trayService.UpdateMenu(viewModel.WindowTitle, entries);
    }

    private void OnTrayShow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnNavigationRequested(object? sender, string pageKey)
    {
        var item = _navigationList.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Tag as string,
                pageKey,
                StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            _navigationList.SelectedItem = item;
        }
    }
}
