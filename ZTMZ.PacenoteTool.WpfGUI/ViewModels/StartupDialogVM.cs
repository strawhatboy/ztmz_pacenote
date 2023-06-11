using System.Threading.Tasks;
using Wpf.Ui.Common;
using Wpf.Ui.Controls.ContentDialogControl;
using Wpf.Ui.Controls.IconElements;
using Wpf.Ui.Controls.Navigation;

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
                dialog.Hide();
            };
            _isInitialized = true;
        }
    }

    [ObservableProperty]
    private string _status = "The status";
}
