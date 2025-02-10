using System.Windows.Controls.Primitives;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.WpfGUI.ViewModels;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class ReplayPlayingPage : INavigableView<ReplayPlayingPageVM>
{
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    public ReplayPlayingPageVM ViewModel { get; }

    public ReplayPlayingPage(ReplayPlayingPageVM viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
        ViewModel.mediaElement = this.videoPlayer;
        this.videoPlayer.MediaOpened += videoPlayer_MediaOpened;
        this.videoPlayer.MediaFailed += videoPlayer_MediaFailed;
    }

    private void videoPlayer_MediaOpened(object sender, System.Windows.RoutedEventArgs e)
    {
        _logger.Info("Media opened, start playing");
        this.videoPlayer.Play();
    }

    private void videoPlayer_MediaFailed(object sender, System.Windows.ExceptionRoutedEventArgs e)
    {
        _logger.Error(e.ErrorException, "Media failed to open");
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


