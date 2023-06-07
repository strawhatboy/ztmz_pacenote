


using Wpf.Ui.Common;
using Wpf.Ui.Controls.IconElements;
using Wpf.Ui.Controls.Navigation;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class MainWindowVM : ObservableObject
{
    bool _isInitialized = false;
    public MainWindowVM()
    {
        if (!_isInitialized) {
            init();
        }
    }

    [ObservableProperty]
    public string _title = "ZTMZ Next Generation Pacenote Tool";

    
    [ObservableProperty]
    private ObservableCollection<object> _navigationItems = new();

    [ObservableProperty]
    private ObservableCollection<object> _navigationFooter = new();

    private void init() {
         NavigationItems = new ObservableCollection<object>
            {
                new NavigationViewItem()
                {
                    Content = "Home",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                    // TargetPageType = typeof(Views.Pages.DashboardPage)
                },
                new NavigationViewItem()
                {
                    Content = "General",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Settings20 },
                    // TargetPageType = typeof(Views.Pages.DataPage)
                },
                new NavigationViewItem()
                {
                    Content = "Voices",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.PersonVoice20 },
                    // TargetPageType = typeof(Views.Pages.DataPage)
                },
                new NavigationViewItem()
                {
                    Content = "Playback",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Play20 },
                    // TargetPageType = typeof(Views.Pages.DataPage)
                },
                new NavigationViewItem()
                {
                    Content = "Hud",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Gauge20 },
                    // TargetPageType = typeof(Views.Pages.DataPage)
                },
                new NavigationViewItem()
                {
                    Content = "Game",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Games20 },
                    // TargetPageType = typeof(Views.Pages.DataPage)
                }
            };

        NavigationFooter = new ObservableCollection<object>
            {
                new NavigationViewItem()
                {
                    Content = "About",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Info24 },
                    TargetPageType = typeof(Views.SettingsPage)
                }
            };
        _isInitialized = true;
    }
}

