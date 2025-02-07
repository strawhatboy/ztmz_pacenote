using System.Windows.Controls.Primitives;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class ReplayPlayingPage : INavigableView<ReplayPlayingPageVM>
{
    public ReplayPlayingPageVM ViewModel { get; }

    public ReplayPlayingPage(ReplayPlayingPageVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
        ViewModel.mediaElement = this.videoPlayer;
    }

    private void thumb_DragStarted(object sender, DragStartedEventArgs e) {
        if (ViewModel.SlideStartedCommand.CanExecute(this)) {
            ViewModel.SlideStartedCommand.Execute(this);
        }
    }
    
    private void thumb_DragCompleted(object sender, DragCompletedEventArgs e) {
        if (ViewModel.SlideEndedCommand.CanExecute(this.sl_video.Value)) {
            ViewModel.SlideEndedCommand.Execute(this.sl_video.Value);
        }
    }
}


