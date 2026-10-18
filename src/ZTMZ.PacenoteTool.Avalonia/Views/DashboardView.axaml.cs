using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using ZTMZ.PacenoteTool.Avalonia.ViewModels;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public sealed partial class DashboardView : UserControl
{
    public DashboardView() => AvaloniaXamlLoader.Load(this);

    private async void CopyOverlayDiagnostics_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel { HasOverlayFailure: true } viewModel) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                ?? throw new InvalidOperationException("Clipboard is unavailable");
            await clipboard.SetTextAsync(viewModel.OverlayFailureDiagnostics);
        }
        catch (Exception error)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(error, "Could not copy overlay diagnostics.");
        }
    }
}
