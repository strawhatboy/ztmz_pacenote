using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class VoicePackagePage : INavigableView<VoicePackagePageVM>
{
    public VoicePackagePageVM ViewModel { get; }

    public VoicePackagePage(VoicePackagePageVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}

