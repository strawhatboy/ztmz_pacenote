using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.WpfGUI.Services;
using ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;
// using Wpf.Ui.Controls.Window;

namespace ZTMZ.PacenoteTool.WpfGUI.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow, INavigationWindow
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        public ViewModels.MainWindowVM ViewModel { get; }
        private IContentDialogService _contentDialogService;
        private readonly ContextMenu _trayContextMenu;
        private readonly NotifyIconService _trayIconService;
        private readonly SystemNotificationService _systemNotificationService;
        private readonly bool _startMinimizedToTray = Config.Instance.StartMinimizedToTray;
        private bool _isTrayExitRequested;
        private bool _isClosed;
        private int _trayRegistrationAttempts;
        private SystemNotificationMessage? _pendingUpdateNotification;
        public MainWindow(ViewModels.MainWindowVM viewModel,
            INavigationViewPageProvider pageService,
            INavigationService navigationService,
            IContentDialogService contentDialogService,
            SystemNotificationService systemNotificationService)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            _trayContextMenu = new ContextMenu
            {
                DataContext = ViewModel,
                ItemsSource = ViewModel.TrayMenuItems
            };
            _trayContextMenu.SetResourceReference(Control.FontFamilyProperty, "AppFontFamily");
            _trayContextMenu.SetResourceReference(Control.FontSizeProperty, "AppFontSize");
            _trayIconService = new MainWindowTrayIconService(ShowAndActivateWindow)
            {
                TooltipText = ViewModel.Title,
                Icon = CreateTrayIcon(),
                ContextMenu = _trayContextMenu
            };
            _systemNotificationService = systemNotificationService;
            _systemNotificationService.NotificationRequested += OnSystemNotificationRequested;
            ViewModel.TrayWindowActionRequested += OnTrayWindowActionRequested;
            ViewModel.UpdateAvailableInTray += OnUpdateAvailableInTray;
            SetPageService(pageService);
            contentDialogService.SetDialogHost(RootContentDialog);
            navigationService.SetNavigationControl(RootNavigation);
            _contentDialogService = contentDialogService;

        }

        private void RegisterTrayIcon()
        {
            if (_isClosed || _trayIconService.IsRegistered)
            {
                return;
            }

            _trayRegistrationAttempts++;
            var isRegistered = _trayIconService.Register();
            Logger.Info("Tray icon registration attempt {0} state: {1}", _trayRegistrationAttempts, isRegistered);

            if (isRegistered || _trayRegistrationAttempts >= 3)
            {
                if (!isRegistered)
                {
                    Logger.Error("Failed to register the system tray icon after {0} attempts.", _trayRegistrationAttempts);
                }
                else if (_startMinimizedToTray)
                {
                    HideOnStartup();
                }

                ShowPendingUpdateNotification();

                return;
            }

            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RegisterTrayIcon));
        }

        private static BitmapImage CreateTrayIcon()
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/Assets/app.png", UriKind.Absolute);
            image.DecodePixelWidth = 32;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // theme
            if (Config.Instance.UseSystemTheme)
            {
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
                var systemTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetSystemTheme();
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(systemTheme == Wpf.Ui.Appearance.SystemTheme.Dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light, WindowBackdropType.Mica, false);
            }
            else
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Config.Instance.IsDarkTheme ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light,
                    WindowBackdropType.Mica,
                    false
                );
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(ThemeHelper.GetAccentColor(),
                    Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            }

            RegisterTrayIcon();
            ViewModel.RefreshTrayMenu();
        }

        private void HideOnStartup()
        {
            if (_isClosed || !_startMinimizedToTray || !_trayIconService.IsRegistered)
            {
                return;
            }

            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SetCurrentValue(WindowStateProperty, WindowState.Minimized);
            Hide();
        }

        private void OnTrayWindowActionRequested(ViewModels.TrayWindowAction action)
        {
            switch (action)
            {
                case ViewModels.TrayWindowAction.ShowMainUi:
                    ShowAndActivateWindow();
                    break;
                case ViewModels.TrayWindowAction.ShowUpdate:
                    ShowAndActivateWindow();
                    _ = Dispatcher.BeginInvoke(
                        DispatcherPriority.ContextIdle,
                        new Action(() => _ = ViewModel.ShowPendingUpdateDialogAsync()));
                    break;
                case ViewModels.TrayWindowAction.ManageCodrivers:
                    ShowAndActivateWindow();
                    Navigate(typeof(VoicePage));
                    break;
                case ViewModels.TrayWindowAction.Exit:
                    _isTrayExitRequested = true;
                    Close();
                    break;
            }
        }

        private void ShowAndActivateWindow()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ShowAndActivateWindow));
                return;
            }

            ShowInTaskbar = true;
            Show();

            // A hidden minimized WPF window must be shown before its state is restored.
            // Restoring it first can leave its taskbar button marked as minimized.
            if (WindowState == WindowState.Minimized)
            {
                SetCurrentValue(WindowStateProperty, WindowState.Normal);
            }

            _ = Activate();
            _ = Focus();
        }

        private void OnSystemNotificationRequested(SystemNotificationMessage notification)
        {
            if (_isClosed)
            {
                return;
            }

            try
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        if (_isClosed || !_trayIconService.IsRegistered)
                        {
                            return;
                        }

                        if (!WindowsTrayNotification.TryShow(_trayIconService, notification))
                        {
                            Logger.Debug("Windows tray notification was not shown.");
                        }
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception, "Failed to dispatch a Windows tray notification.");
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // The dispatcher has already shut down.
            }
        }

        private void OnUpdateAvailableInTray(string version)
        {
            if (_isClosed)
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => OnUpdateAvailableInTray(version)));
                return;
            }

            _pendingUpdateNotification = new SystemNotificationMessage(
                I18NLoader.Instance["dialog.newUpdate.title"],
                string.Format(I18NLoader.Instance["dialog.newUpdate.content"], version, UpdateService.CurrentVersion));
            ShowPendingUpdateNotification();
        }

        private void ShowPendingUpdateNotification()
        {
            if (_pendingUpdateNotification == null || !_trayIconService.IsRegistered)
            {
                return;
            }

            var notification = _pendingUpdateNotification;
            _pendingUpdateNotification = null;
            OnSystemNotificationRequested(notification);
        }

        public async void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_isTrayExitRequested || App.IsDataFolderMigrationShutdownRequested)
                return;

            if (Config.Instance.ShowClosePrompt)
            {
                e.Cancel = true;
                ClosePrompt closePrompt = new ClosePrompt(_contentDialogService.GetDialogHostEx()!);
                ContentDialogResult result = await closePrompt.ShowAsync();
                if (result == ContentDialogResult.None)
                {
                    e.Cancel = true;
                    return;
                }
                if (result == ContentDialogResult.Primary)
                {
                    Config.Instance.CloseWindowToMinimize = closePrompt.CloseToMinimize;
                    Config.Instance.ShowClosePrompt = !closePrompt.DontShowAgain;
                    Config.Instance.SaveUserConfig();
                }
            }
            if (Config.Instance.CloseWindowToMinimize)
            {
                e.Cancel = true;
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                this.WindowState = WindowState.Minimized;
                Hide();
            }
            else
            {
                ObsManager.Instance.AbnormallyStopRecording();
                Application.Current.Shutdown();
            }
        }

        public void CloseWindow()
        {
            this.Close();
        }
        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            ViewModel.TrayWindowActionRequested -= OnTrayWindowActionRequested;
            ViewModel.UpdateAvailableInTray -= OnUpdateAvailableInTray;
            _systemNotificationService.NotificationRequested -= OnSystemNotificationRequested;
            _trayIconService.Unregister();
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            ObsManager.Instance.AbnormallyStopRecording();
            Application.Current.Shutdown();
        }

        private sealed class MainWindowTrayIconService : NotifyIconService
        {
            private readonly Action _showMainWindow;

            public MainWindowTrayIconService(Action showMainWindow)
            {
                _showMainWindow = showMainWindow;
            }

            protected override void OnLeftClick()
            {
                _showMainWindow();
            }
        }

        public INavigationView GetNavigation()
        {
            return this.RootNavigation;
        }

        public bool Navigate(Type pageType)
        {
            return this.RootNavigation.Navigate(pageType);
        }

        public void SetPageService(INavigationViewPageProvider pageService)
        {
            this.RootNavigation.SetPageProviderService(pageService);
        }

        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            this.RootNavigation.SetServiceProvider(serviceProvider);
        }

        public void ShowWindow()
        {
            Application.Current.MainWindow = this;
            if (_startMinimizedToTray)
            {
                SetCurrentValue(WindowStateProperty, WindowState.Minimized);
            }

            Show();
        }

        // Remove navigation header when in Home page.
        private void OnNavigationSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.NavigationView navigationView)
            {
                return;
            }

            var isHomePage = navigationView.SelectedItem?.TargetPageType == typeof(HomePage);

            RootNavigation.HeaderVisibility = isHomePage
                ? Visibility.Collapsed
                : Visibility.Visible;

            // HomePage owns its scroll surface so its diagnostics row can remain pinned.
            // Other pages retain NavigationView's normal automatic scroll behavior.
            SetNavigationDynamicScrollEnabled(!isHomePage);
        }

        private void SetNavigationDynamicScrollEnabled(bool isEnabled)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var presenter = FindVisualChild<NavigationViewContentPresenter>(RootNavigation);
                if (presenter != null)
                {
                    presenter.SetValue(
                        NavigationViewContentPresenter.IsDynamicScrollViewerEnabledProperty,
                        isEnabled);
                }
            }));
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T result)
                {
                    return result;
                }

                var nestedResult = FindVisualChild<T>(child);
                if (nestedResult != null)
                {
                    return nestedResult;
                }
            }

            return null;
        }
    }
}
