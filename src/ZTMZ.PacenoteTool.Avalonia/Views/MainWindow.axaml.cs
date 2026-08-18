using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Avalonia.ViewModels;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public sealed partial class MainWindow : FAAppWindow
{
    private readonly ListBox _navigationList;
    private readonly ContentControl _pageHost;
    private readonly IReadOnlyDictionary<string, Control> _pages;
    private readonly GeneralSettingsViewModel _generalSettingsViewModel;
    private MainWindowViewModel? _viewModel;
    private bool _exitRequested;
    private bool _closeFlowActive;
#if WINDOWS
    private readonly WindowsTrayService? _trayService;
#endif

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var customTitleBar = this.FindControl<Grid>("CustomTitleBar") ??
            throw new InvalidOperationException("Custom title bar was not created.");
        TitleBar.ExtendsContentIntoTitleBar = true;
        customTitleBar.IsVisible = true;

        _navigationList = this.FindControl<ListBox>("NavigationList") ??
            throw new InvalidOperationException("Navigation list was not created.");
        _pageHost = this.FindControl<ContentControl>("PageHost") ??
            throw new InvalidOperationException("Page host was not created.");
        var homeNavigationItem = this.FindControl<ListBoxItem>("HomeNavigationItem") ??
            throw new InvalidOperationException("Home navigation item was not created.");

        _generalSettingsViewModel = new GeneralSettingsViewModel();
        _generalSettingsViewModel.NotificationRequested += OnSystemNotificationRequested;
        _pages = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = new HomeView(),
            ["general"] = new GeneralView { DataContext = _generalSettingsViewModel },
            ["codriver"] = new CoDriverView(),
            ["dashboard"] = new DashboardView()
        };

#if WINDOWS
        var trayIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app_tray.ico");
        if (File.Exists(trayIconPath))
        {
            _trayService = new WindowsTrayService(trayIconPath);
            _trayService.ShowRequested += OnTrayShowRequested;
            _trayService.ExitRequested += OnTrayExitRequested;
            UpdateTrayText();
        }
#endif

        _navigationList.SelectionChanged += OnNavigationSelectionChanged;
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        AvaloniaLocalization.CultureChanged += OnCultureChanged;
        _navigationList.SelectedItem = homeNavigationItem;
        _pageHost.Content = _pages["home"];
    }

    private void OnNavigationSelectionChanged(
        object? sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_navigationList.SelectedItem is ListBoxItem item &&
            item.Tag is string pageKey &&
            _pages.TryGetValue(pageKey, out var page))
        {
            _pageHost.Content = page;
            if (string.Equals(pageKey, "general", StringComparison.OrdinalIgnoreCase))
            {
                _generalSettingsViewModel.RefreshFromConfig();
            }
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        DetachViewModel();
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel != null)
        {
            _viewModel.NavigationRequested += OnNavigationRequested;
            _viewModel.SystemNotificationRequested += OnSystemNotificationRequested;
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.NavigationRequested -= OnNavigationRequested;
            _viewModel.SystemNotificationRequested -= OnSystemNotificationRequested;
            _viewModel = null;
        }
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
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

        var dialog = new FAContentDialog
        {
            Title = AvaloniaLocalization.Get("ui.closeBehavior"),
            Content = content,
            PrimaryButtonText = AvaloniaLocalization.Get("dialog.closePrompt.ok"),
            CloseButtonText = AvaloniaLocalization.Get("dialog.closePrompt.cancel"),
            DefaultButton = FAContentDialogButton.Primary
        };
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

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        AvaloniaLocalization.CultureChanged -= OnCultureChanged;
        _generalSettingsViewModel.NotificationRequested -= OnSystemNotificationRequested;
        _generalSettingsViewModel.Dispose();
        DetachViewModel();
#if WINDOWS
        if (_trayService != null)
        {
            _trayService.ShowRequested -= OnTrayShowRequested;
            _trayService.ExitRequested -= OnTrayExitRequested;
            _trayService.Dispose();
        }
#endif
    }

    private void OnCultureChanged(object? sender, EventArgs eventArgs)
    {
#if WINDOWS
        UpdateTrayText();
#endif
    }

    private void OnSystemNotificationRequested(string title, string message)
    {
#if WINDOWS
        _trayService?.ShowNotification(title, message);
#endif
    }

#if WINDOWS
    private void OnTrayShowRequested(object? sender, EventArgs eventArgs)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnTrayExitRequested(object? sender, EventArgs eventArgs)
    {
        _exitRequested = true;
        Close();
    }

    private void UpdateTrayText() => _trayService?.UpdateText(
        _viewModel?.WindowTitle ?? Title ?? "ZTMZ",
        AvaloniaLocalization.Get("ui.showMainUI"),
        AvaloniaLocalization.Get("ui.closeToExit"));
#endif

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
