using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class VoiceSettingsPage : INavigableView<VoiceSettingsPageVM>
{
    public VoiceSettingsPageVM ViewModel { get; }

    public VoiceSettingsPage(VoiceSettingsPageVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}

