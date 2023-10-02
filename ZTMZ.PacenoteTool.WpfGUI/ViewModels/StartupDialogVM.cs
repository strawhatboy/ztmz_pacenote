using System.Threading.Tasks;
using Wpf.Ui;
using Wpf.Ui.Controls;
// using Wpf.Ui.Controls.IconElements;
// using Wpf.Ui.Controls.Navigation;

using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WpfGUI.Views;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels;

public partial class StartupDialogVM : ObservableObject
{
    bool _isInitialized = false;


    public StartupDialogVM(ContentDialog dialog, ZTMZPacenoteTool tool)
    {
        if (!_isInitialized) {

            tool.onStatusReport += s => {
                Status = s;
            };
            tool.onToolInitialized += () => {
                Application.Current.Dispatcher.Invoke(() => {
                    dialog.Hide();
                });
            };
            _isInitialized = true;
        }
    }

    [ObservableProperty]
    private string _status = "The status";
}
