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
        public MainWindow(ViewModels.MainWindowVM viewModel,
            IPageService pageService,
            INavigationService navigationService,
            IContentDialogService contentDialogService)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            SetPageService(pageService);
            contentDialogService.SetContentPresenter(RootContentDialog);
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
        public async void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (Config.Instance.ShowClosePrompt)
            {
                e.Cancel = true;
                ClosePrompt closePrompt = new ClosePrompt(_contentDialogService.GetDialogHost());
                ContentDialogResult result = await closePrompt.ShowAsync();
                if (result == ContentDialogResult.None)
                {
                    e.Cancel = true;
                    return;
                }
                if (result == ContentDialogResult.Primary)
                {
                    Config.Instance.CloseWindowToMinimize = closePrompt.CloseToMinimize;
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

        public void SetPageService(IPageService pageService)
        {
            this.RootNavigation.SetPageService(pageService);
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
