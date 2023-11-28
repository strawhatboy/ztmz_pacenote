using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class StartupDialog : ContentDialog
{
    public ViewModels.StartupDialogVM ViewModel { get; private set; }
    public StartupDialog(ContentPresenter contentPresenter, ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool) : base(contentPresenter)
    {
        ViewModel = new ViewModels.StartupDialogVM(this, _tool);
        DataContext = this;
        InitializeComponent();
    }

}
