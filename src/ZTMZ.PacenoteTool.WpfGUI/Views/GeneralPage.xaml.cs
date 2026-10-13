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

    private void OnAccentColorPresetClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement { Tag: System.Windows.Media.Color color })
        {
            AccentColorButton.IsChecked = false;
            ViewModel.SetAccentColor(color);
        }
    }

    private void OnAccentColorPopupKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            AccentColorButton.IsChecked = false;
            AccentColorButton.Focus();
            e.Handled = true;
        }
    }
}


