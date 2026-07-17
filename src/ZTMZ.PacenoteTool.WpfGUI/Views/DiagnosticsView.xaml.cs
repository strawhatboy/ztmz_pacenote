using System.Windows.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsViewVM ViewModel { get; }

    public DiagnosticsView(DiagnosticsViewVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
