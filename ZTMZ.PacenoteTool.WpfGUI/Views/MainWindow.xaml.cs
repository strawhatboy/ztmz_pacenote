using System;
using System.Collections.Generic;
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
// using Wpf.Ui.Controls.Window;

namespace ZTMZ.PacenoteTool.WpfGUI.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow, INavigationWindow
    {
        public ViewModels.MainWindowVM ViewModel { get; }
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

        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // theme
            
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(ThemeHelper.GetAccentColor(), 
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());

            if (Config.Instance.UseSystemTheme) {
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            } else {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Config.Instance.IsDarkTheme ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light,
                    WindowBackdropType.Mica,
                    false
                );
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
