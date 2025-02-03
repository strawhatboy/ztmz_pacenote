using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class ReplayPage : INavigableView<ReplayPageVM>
{
    public ReplayPageVM ViewModel { get; }

    public ReplayPage(ReplayPageVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}


