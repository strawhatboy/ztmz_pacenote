using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

public class CustomNotifyIconService : NotifyIconService
{
    public CustomNotifyIconService()
    {
        TooltipText = "DEMO";
        // If this icon is not defined, the application icon will be used.
        Icon = BitmapFrame.Create(
            new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute)
        );
        ContextMenu = new ContextMenu
        {
            FontSize = 14d,
            Items =
        {
            new Wpf.Ui.Controls.MenuItem
            {
                Header = I18NLoader.Instance["ui.showMainUI"],
                Icon = new SymbolIcon { Symbol = SymbolRegular.Home12 },
                Tag = I18NLoader.Instance["ui.showMainUI"],
                Command=new RelayCommand(() => {
                    Application.Current.MainWindow?.Show();
                })
            },
            new Separator(),
            new Wpf.Ui.Controls.MenuItem
            {
                Header = I18NLoader.Instance["ui.closeToExit"],
                Icon = new SymbolIcon { Symbol = SymbolRegular.ClosedCaption16 },
                Tag = I18NLoader.Instance["ui.closeToExit"],
                Command=new RelayCommand(() => App.Current.Shutdown())
            },
        }
        };
        foreach (var singleContextMenuItem in ContextMenu.Items)
            if (singleContextMenuItem is System.Windows.Controls.MenuItem)
                ((System.Windows.Controls.MenuItem)singleContextMenuItem).Click += OnMenuItemClick;

    }

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem menuItem)
            return;

        System.Diagnostics.Debug.WriteLine(
            $"DEBUG | WPF UI Tray clicked: {menuItem.Tag}",
            "Wpf.Ui.Demo"
        );
    }
}
