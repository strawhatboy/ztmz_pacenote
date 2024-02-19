using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;

public partial class StartupDialog : ContentDialog
{
    public ViewModels.Dialog.StartupDialogVM ViewModel { get; private set; }
    public StartupDialog(ContentPresenter contentPresenter, ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool) : base(contentPresenter)
    {
        ViewModel = new ViewModels.Dialog.StartupDialogVM(this, _tool);
        DataContext = this;
        InitializeComponent();
    }

}
