using Wpf.Ui.Controls.Navigation;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class HomePage : INavigableView<HomePageVM>
{
    public HomePageVM ViewModel { get; }

    public HomePage(HomePageVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}


