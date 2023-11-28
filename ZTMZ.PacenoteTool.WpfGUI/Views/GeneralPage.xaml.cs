using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class GeneralPage : INavigableView<GeneralPageVM>
{
    public GeneralPageVM ViewModel { get; }

    public GeneralPage(GeneralPageVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}


