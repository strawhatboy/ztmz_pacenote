using Wpf.Ui.Controls;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class ReplayWindow : FluentWindow
{
    public ViewModels.ReplayWindowVM ViewModel { get; }

    public ReplayWindow(ViewModels.ReplayWindowVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}
