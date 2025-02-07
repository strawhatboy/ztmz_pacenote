using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class ReplaySettingsPage : INavigableView<ReplaySettingsPageVM>
{
    public ReplaySettingsPageVM ViewModel { get; }

    public ReplaySettingsPage(ReplaySettingsPageVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}


