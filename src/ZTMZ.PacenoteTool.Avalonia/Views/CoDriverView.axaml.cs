using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Avalonia.ViewModels;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public sealed partial class CoDriverView : UserControl
{
    public CoDriverView()
    {
        AvaloniaXamlLoader.Load(this);
        AvaloniaLocalization.CultureChanged += OnCultureChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        UpdateVoicePackageDetailsHeaders();
    }

    private void OnCultureChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(UpdateVoicePackageDetailsHeaders);

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        AvaloniaLocalization.CultureChanged -= OnCultureChanged;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
    }

    private void UpdateVoicePackageDetailsHeaders()
    {
        var dataGrid = this.FindControl<DataGrid>("VoicePackageDetailsGrid");
        if (dataGrid == null || dataGrid.Columns.Count < 7)
            return;

        var keys = new[]
        {
            "voicePackageDetails.id",
            "voicePackageDetails.token",
            "voicePackageDetails.description",
            "voicePackageDetails.available",
            "voicePackageDetails.type",
            "voicePackageDetails.files",
            "voicePackageDetails.listen"
        };
        for (var index = 0; index < keys.Length; index++)
            dataGrid.Columns[index].Header = AvaloniaLocalization.Get(keys[index]);
    }

    private void OnVoicePackageCardPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Border { Tag: VoicePackageChoice package } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // The package card opens from every non-button part of its row. Update/export controls keep their own clicks.
        for (var visual = eventArgs.Source as Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is Button)
                return;
        }

        if (viewModel.OpenVoicePackageDetailsCommand.CanExecute(package))
        {
            viewModel.OpenVoicePackageDetailsCommand.Execute(package);
            eventArgs.Handled = true;
        }
    }

    private async void OnImportVoicePackageClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
            return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("ZTMZ voice package") { Patterns = ["*.zpak"] }]
        });
        if (files.Count == 0)
            return;

        var archivePath = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(archivePath))
            return;

        try
        {
            var plan = await viewModel.InspectVoicePackageImportAsync(archivePath);
            if (plan.DestinationExists)
            {
                var dialog = FaContentDialogFactory.Create(new FAContentDialog
                {
                    Title = AvaloniaLocalization.Get("dialog.voicePkgExist.title"),
                    Content = string.Format(AvaloniaLocalization.Get("dialog.voicePkgExist.content"), plan.DestinationPath),
                    PrimaryButtonText = AvaloniaLocalization.Get("dialog.voicePkgExist.ok"),
                    CloseButtonText = AvaloniaLocalization.Get("dialog.voicePkgExist.cancel"),
                    DefaultButton = FAContentDialogButton.Primary
                });
                if (await dialog.ShowAsync(TopLevel.GetTopLevel(this)) != FAContentDialogResult.Primary)
                    return;
            }

            await viewModel.ImportVoicePackageAsync(archivePath, plan.DestinationExists ? plan.DestinationPath : null);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void OnExportVoicePackageClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Button { Tag: VoicePackageChoice package } ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
            return;

        var destination = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = package.Name + ".zpak",
            DefaultExtension = "zpak",
            FileTypeChoices = [new FilePickerFileType("ZTMZ voice package") { Patterns = ["*.zpak"] }]
        });
        var archivePath = destination?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(archivePath))
            return;

        try
        {
            await viewModel.ExportVoicePackageAsync(package, archivePath);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void OnPickAdditionalCoDriverDirectoryClick(object? sender, RoutedEventArgs eventArgs)
    {
        var directory = await PickDirectoryAsync();
        if (!string.IsNullOrWhiteSpace(directory) && DataContext is MainWindowViewModel viewModel)
            viewModel.VoiceSettings.AdditionalCoDriverPackagesSearchPath = directory;
    }

    private async void OnPickAdditionalPacenotesDirectoryClick(object? sender, RoutedEventArgs eventArgs)
    {
        var directory = await PickDirectoryAsync();
        if (!string.IsNullOrWhiteSpace(directory) && DataContext is MainWindowViewModel viewModel)
            viewModel.VoiceSettings.AdditionalPacenotesDefinitionSearchPath = directory;
    }

    private async void OnPickCustomPacenoteDatabaseClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not MainWindowViewModel viewModel)
            return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("ZTMZ pacenote database") { Patterns = ["*.zdb"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            viewModel.VoiceSettings.CustomPacenoteDatabasePath = path;
    }

    private async Task<string?> PickDirectoryAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
            return null;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = AvaloniaLocalization.Get("scriptEditor.errorTitle"),
            Content = message,
            CloseButtonText = AvaloniaLocalization.Get("dialog.common.btn_ok")
        });
        await dialog.ShowAsync(TopLevel.GetTopLevel(this));
    }
}
