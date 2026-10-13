using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Avalonia.ViewModels;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public partial class ReplayExportView : UserControl
{
    public ReplayExportView() => AvaloniaXamlLoader.Load(this);
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReplayExportViewModel vm || vm.SelectedReplay == null || vm.IsBusy) return;
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = AvaloniaLocalization.Get("ui.replayExportMotec"), AllowMultiple = false
            });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) await vm.ExportAsync(path);
        }
        catch (Exception ex) { vm.Message = ex.Message; }
    }
}
