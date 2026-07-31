using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WpfGUI.Services;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class DataFolderMigrationWindow : FluentWindow
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly DataFolderMigrationRequest _request;
    private readonly DataFolderMigrationService _migrationService = new();
    private bool _isFinished;
    private bool _isDeletingSource;

    internal DataFolderMigrationWindow(DataFolderMigrationRequest request)
    {
        _request = request;
        InitializeComponent();
        InitializeLanguage();

        Title = Text("dataFolderMigration.title", "Move Data Folder");
        TitleText.Text = Title;
        CloseButton.Content = Text("dataFolderMigration.cancel", "Cancel");
        RestartButton.Content = Text("dataFolderMigration.restart", "Restart application");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await RunMigrationAsync();
    }

    private async Task RunMigrationAsync()
    {
        try
        {
            SetStatus(Text("dataFolderMigration.waitingForApplication", "Waiting for the application to close..."));
            await WaitForParentProcessExitAsync(_request.ParentProcessId, _cancellation.Token);

            SetStatus(Text("dataFolderMigration.scanning", "Checking source files and destination space..."));
            var plan = await Task.Run(
                () => _migrationService.CreatePlan(_request.SourcePath, _request.DestinationPath, _cancellation.Token),
                _cancellation.Token);

            var progress = new Progress<DataFolderMigrationProgress>(OnMigrationProgress);
            SetStatus(Text("dataFolderMigration.copying", "Copying data files..."));
            await Task.Run(
                () => _migrationService.CopyAndVerifyAsync(plan, progress, _cancellation.Token),
                _cancellation.Token);

            AppLevelVariables.Instance.SetAppConfigFolderForNextLaunch(plan.DestinationPath);
            _isFinished = true;
            MigrationProgress.Value = 100;
            SetStatus(Text("dataFolderMigration.complete", "Migration completed. The new data folder will be used on the next start."));
            DetailText.Text = plan.DestinationPath;
            CloseButton.Content = Text("dataFolderMigration.close", "Close");
            RestartButton.Visibility = Visibility.Visible;

            if (AskToDeleteSource(plan.SourcePath))
            {
                await TryDeleteSourceAsync(plan);
            }
        }
        catch (OperationCanceledException)
        {
            FinishWithoutMigration(
                Text("dataFolderMigration.canceled", "Migration canceled. The original data folder remains active. Any partial destination files were left untouched."));
        }
        catch (InsufficientDataFolderMigrationSpaceException exception)
        {
            FinishWithoutMigration(string.Format(
                Text("dataFolderMigration.insufficientSpace", "The destination has {1} free, but {0} is required."),
                FormatSize(exception.RequiredBytes),
                FormatSize(exception.AvailableBytes)));
        }
        catch (Exception exception)
        {
            if (_isFinished)
            {
                DetailText.Text = exception.Message;
                return;
            }

            FinishWithoutMigration(string.Format(
                Text("dataFolderMigration.failed", "Migration failed. The original data folder remains active.\n\n{0}"),
                exception.Message));
        }
    }

    private async Task WaitForParentProcessExitAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            // The main application already exited before the helper reached this point.
        }
    }

    private void OnMigrationProgress(DataFolderMigrationProgress progress)
    {
        if (_isFinished)
        {
            return;
        }

        var fraction = progress.TotalBytes == 0
            ? 1d
            : Math.Clamp(progress.BytesProcessed / (double)progress.TotalBytes, 0d, 1d);
        MigrationProgress.Value = progress.Phase == DataFolderMigrationPhase.Copying
            ? fraction * 50d
            : 50d + fraction * 50d;

        SetStatus(progress.Phase == DataFolderMigrationPhase.Copying
            ? Text("dataFolderMigration.copying", "Copying data files...")
            : Text("dataFolderMigration.verifying", "Verifying copied files..."));
        DetailText.Text = progress.RelativePath;
    }

    private bool AskToDeleteSource(string sourcePath)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            string.Format(
                Text("dataFolderMigration.deleteOldConfirmation", "The copied data was verified. Delete the old data folder?\n\n{0}\n\nThis cannot be undone."),
                sourcePath),
            Text("dataFolderMigration.title", "Move Data Folder"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);

        return result == System.Windows.MessageBoxResult.Yes;
    }

    private async Task TryDeleteSourceAsync(DataFolderMigrationPlan plan)
    {
        _isDeletingSource = true;
        CloseButton.IsEnabled = false;
        RestartButton.IsEnabled = false;
        SetStatus(Text("dataFolderMigration.deletingOldFolder", "Rechecking copied files and deleting the old data folder..."));
        try
        {
            await Task.Run(() => _migrationService.DeleteSourceDirectoryAsync(plan));
            DetailText.Text = Text("dataFolderMigration.oldFolderDeleted", "The old data folder was deleted.");
        }
        catch (Exception exception)
        {
            DetailText.Text = string.Format(
                Text("dataFolderMigration.deleteFailed", "The old data folder was kept because it could not be deleted: {0}"),
                exception.Message);
        }
        finally
        {
            _isDeletingSource = false;
            CloseButton.IsEnabled = true;
            RestartButton.IsEnabled = true;
            SetStatus(Text("dataFolderMigration.complete", "Migration completed. The new data folder will be used on the next start."));
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        if (_isFinished)
        {
            Close();
            return;
        }

        CloseButton.IsEnabled = false;
        SetStatus(Text("dataFolderMigration.canceling", "Canceling migration..."));
        _cancellation.Cancel();
    }

    private void OnRestartClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            DataFolderMigrationLauncher.RestartApplication();
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            DetailText.Text = exception.Message;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isDeletingSource)
        {
            e.Cancel = true;
            return;
        }

        if (_isFinished)
        {
            return;
        }

        e.Cancel = true;
        CloseButton.IsEnabled = false;
        SetStatus(Text("dataFolderMigration.canceling", "Canceling migration..."));
        _cancellation.Cancel();
    }

    private void FinishWithoutMigration(string message)
    {
        _isFinished = true;
        SetStatus(message);
        CloseButton.IsEnabled = true;
        CloseButton.Content = Text("dataFolderMigration.close", "Close");
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void InitializeLanguage()
    {
        try
        {
            var languageDirectories = Directory.Exists(_request.SourcePath)
                ? Directory.EnumerateDirectories(_request.SourcePath, Constants.PATH_LANGUAGE, SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(path).Equals("lang", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<string>();
            I18NLoader.Instance.Initialize(languageDirectories);
            I18NLoader.Instance.SetCulture(_request.Language);
        }
        catch (Exception)
        {
            // The migration window has English fallbacks for the case where language resources cannot be loaded.
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        var suffixIndex = 0;
        decimal size = bytes;
        while (suffixIndex < suffixes.Length - 1 && size >= 1024)
        {
            size /= 1024;
            suffixIndex++;
        }

        return string.Format("{0:n1} {1}", size, suffixes[suffixIndex]);
    }

    private static string Text(string key, string fallback)
    {
        try
        {
            var value = I18NLoader.Instance[key];
            return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
