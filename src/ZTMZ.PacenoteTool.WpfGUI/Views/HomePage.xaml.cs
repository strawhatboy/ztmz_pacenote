using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;
using System.Windows.Input;

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

    private void DiagnosticsStatusBar_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.OpenDiagnosticsDialogCommand.CanExecute(null))
        {
            ViewModel.OpenDiagnosticsDialogCommand.Execute(null);
        }
    }
}

