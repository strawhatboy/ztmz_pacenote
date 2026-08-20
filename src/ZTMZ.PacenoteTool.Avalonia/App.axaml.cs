using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Avalonia.ViewModels;
using ZTMZ.PacenoteTool.Avalonia.Views;

namespace ZTMZ.PacenoteTool.Avalonia;

public sealed partial class App : Application
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private MainWindowViewModel? _mainWindowViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AvaloniaLocalization.ApplyCurrentCulture(this);
        AvaloniaAppearanceService.ApplyConfiguredAppearance(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (AvaloniaDataFolderMigrationLauncher.TryParseRequest(
                    Environment.GetCommandLineArgs(), out var migrationRequest))
            {
                desktop.MainWindow = new DataFolderMigrationWindow(migrationRequest!);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            _mainWindowViewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow(_mainWindowViewModel);
            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(
        object? sender,
        ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        try
        {
            _mainWindowViewModel?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to dispose the Avalonia runtime during shutdown.");
        }
    }
}
