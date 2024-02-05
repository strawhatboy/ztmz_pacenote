
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class VRPage : INavigableView<VRPageVM>
{
    public VRPageVM ViewModel { get; }

    public VRPage(VRPageVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}

