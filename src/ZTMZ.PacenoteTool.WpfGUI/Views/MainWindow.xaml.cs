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
using Wpf.Ui;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;
// using Wpf.Ui.Controls.Window;

namespace ZTMZ.PacenoteTool.WpfGUI.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow, INavigationWindow
    {
        public ViewModels.MainWindowVM ViewModel { get; }
        private IContentDialogService _contentDialogService;
        private bool _isTrayExitRequested;
        public MainWindow(ViewModels.MainWindowVM viewModel,
            INavigationViewPageProvider pageService,
            INavigationService navigationService,
            IContentDialogService contentDialogService)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            SetupTrayMenuEvents();
            SetPageService(pageService);
            contentDialogService.SetDialogHost(RootContentDialog);
            navigationService.SetNavigationControl(RootNavigation);
            _contentDialogService = contentDialogService;

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
        }

        private void SetupTrayMenuEvents()
        {
            foreach (var menuItem in ViewModel.TrayMenuItems)
            {
                if (menuItem is Wpf.Ui.Controls.MenuItem item)
                    item.Click += OnTrayMenuItemClick;
            }
        }

        private void OnTrayMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.MenuItem menuItem)
                return;

            switch (menuItem.Tag?.ToString())
            {
                case "tray_home":
                    ShowAndActivateWindow();
                    break;
                case "tray_close":
                    _isTrayExitRequested = true;
                    Close();
                    break;
            }
        }

        private void ShowAndActivateWindow()
        {
            if (WindowState == WindowState.Minimized)
                SetCurrentValue(WindowStateProperty, WindowState.Normal);

            Show();
            _ = Activate();
            _ = Focus();
        }

        public async void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_isTrayExitRequested)
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
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            ObsManager.Instance.AbnormallyStopRecording();
            Application.Current.Shutdown();
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
            this.Show();
        }

        // Remove navigation header when in Home page.
        private void OnNavigationSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.NavigationView navigationView)
            {
                return;
            }

            RootNavigation.HeaderVisibility =
                RootNavigation.SelectedItem?.TargetPageType != typeof(HomePage)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
}
