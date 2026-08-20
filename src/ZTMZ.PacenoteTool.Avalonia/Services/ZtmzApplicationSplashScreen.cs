using Avalonia.Media;
using FluentAvalonia.UI.Windowing;
using ZTMZ.PacenoteTool.Avalonia.ViewModels;
using ZTMZ.PacenoteTool.Avalonia.Views;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal sealed class ZtmzApplicationSplashScreen : IFAApplicationSplashScreen
{
    private readonly MainWindowViewModel _viewModel;
    private readonly StartupSplashContent _content;

    public ZtmzApplicationSplashScreen(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _content = new StartupSplashContent { DataContext = viewModel };
    }

    public string AppName => string.Empty;

    public IImage AppIcon => null!;

    public object SplashScreenContent => _content;

    public int MinimumShowTime => 350;

    public Task RunTasks(CancellationToken cancellationToken) =>
        _viewModel.InitializeAsync(cancellationToken);
}
