#nullable enable

using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Windowing;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public sealed partial class DataFolderMigrationWindow : FAAppWindow
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly AvaloniaDataFolderMigrationRequest _request;
    private readonly DataFolderMigrationService _migrationService = new();
    private bool _finished;

    internal DataFolderMigrationWindow(AvaloniaDataFolderMigrationRequest request)
    {
        _request = request;
        AvaloniaXamlLoader.Load(this);
        Title = Text("dataFolderMigration.title");
        TitleText.Text = Title;
        CloseButton.Content = Text("dataFolderMigration.cancel");
        RestartButton.Content = Text("dataFolderMigration.restart");
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        Opened -= OnOpened;
        try
        {
            SetStatus(Text("dataFolderMigration.waitingForApplication"));
            await WaitForParentProcessExitAsync(_request.ParentProcessId, _cancellation.Token);
            SetStatus(Text("dataFolderMigration.scanning"));
            var plan = await Task.Run(
                () => _migrationService.CreatePlan(_request.SourcePath, _request.DestinationPath, _cancellation.Token),
                _cancellation.Token);
            var progress = new Progress<DataFolderMigrationProgress>(UpdateProgress);
            SetStatus(Text("dataFolderMigration.copying"));
            await _migrationService.CopyAndVerifyAsync(plan, progress, _cancellation.Token);
            AppLevelVariables.Instance.SetAppConfigFolderForNextLaunch(plan.DestinationPath);
            _finished = true;
            MigrationProgress.Value = 100;
            SetStatus(Text("dataFolderMigration.complete"));
            DetailText.Text = plan.DestinationPath;
            CloseButton.Content = Text("dataFolderMigration.close");
            RestartButton.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            FinishWithoutMigration(Text("dataFolderMigration.canceled"));
        }
        catch (InsufficientDataFolderMigrationSpaceException exception)
        {
            FinishWithoutMigration(string.Format(
                Text("dataFolderMigration.insufficientSpace"),
                FormatSize(exception.RequiredBytes), FormatSize(exception.AvailableBytes)));
        }
        catch (Exception exception)
        {
            FinishWithoutMigration(string.Format(Text("dataFolderMigration.failed"), exception.Message));
        }
    }

    private static async Task WaitForParentProcessExitAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            // The application had already stopped before the helper opened its window.
        }
    }

    private void UpdateProgress(DataFolderMigrationProgress progress)
    {
        var fraction = progress.TotalBytes == 0 ? 1d : Math.Clamp(progress.BytesProcessed / (double)progress.TotalBytes, 0d, 1d);
        MigrationProgress.Value = progress.Phase == DataFolderMigrationPhase.Copying ? fraction * 50d : 50d + fraction * 50d;
        SetStatus(Text(progress.Phase == DataFolderMigrationPhase.Copying
            ? "dataFolderMigration.copying"
            : "dataFolderMigration.verifying"));
        DetailText.Text = progress.RelativePath;
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (_finished)
        {
            Close();
            return;
        }

        CloseButton.IsEnabled = false;
        SetStatus(Text("dataFolderMigration.canceling"));
        _cancellation.Cancel();
    }

    private void Restart_Click(object? sender, RoutedEventArgs eventArgs)
    {
        AvaloniaDataFolderMigrationLauncher.RestartApplication();
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_finished)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        CloseButton.IsEnabled = false;
        SetStatus(Text("dataFolderMigration.canceling"));
        _cancellation.Cancel();
    }

    private void FinishWithoutMigration(string message)
    {
        _finished = true;
        SetStatus(message);
        CloseButton.IsEnabled = true;
        CloseButton.Content = Text("dataFolderMigration.close");
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private static string Text(string key) => AvaloniaLocalization.Get(key);

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        decimal value = bytes;
        while (unit < suffixes.Length - 1 && value >= 1024)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:n1} {suffixes[unit]}";
    }
}
