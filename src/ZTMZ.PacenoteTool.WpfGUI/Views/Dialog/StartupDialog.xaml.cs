using System.Windows;
using Wpf.Ui.Controls;

namespace ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;

public partial class StartupDialog : ContentDialog
{
    public ViewModels.Dialog.StartupDialogVM ViewModel { get; private set; }
    public StartupDialog(ContentDialogHost dialogHost, ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool _tool) : base(dialogHost)
    {
        ViewModel = new ViewModels.Dialog.StartupDialogVM(_tool);
        DataContext = this;
        InitializeComponent();
    }

}
