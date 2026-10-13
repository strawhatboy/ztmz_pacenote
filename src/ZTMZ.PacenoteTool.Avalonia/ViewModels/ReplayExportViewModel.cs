using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.Avalonia.ViewModels;

internal sealed record ReplayExportChoice(int Id, string Label);

internal sealed partial class ReplayExportViewModel(IDesktopRuntime runtime) : ObservableObject
{
    private readonly Lazy<ReplayCatalogService> catalog = new(() => new ReplayCatalogService());
    private int loadVersion;
    public ObservableCollection<IGame> Games { get; } = [];
    public ObservableCollection<ReplayExportChoice> Replays { get; } = [];
    [ObservableProperty] private IGame? selectedGame;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanExport))] private ReplayExportChoice? selectedReplay;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanExport))] private bool isBusy;
    public bool CanExport => !IsBusy && SelectedReplay != null;
    [ObservableProperty] private string message = string.Empty;
    public bool RecordTelemetry
    {
        get => Config.Instance.ReplaySave;
        set { Config.Instance.ReplaySave = value; Config.Instance.SaveUserConfig(); OnPropertyChanged(); }
    }

    partial void OnSelectedGameChanged(IGame? value) => _ = LoadReplaysAsync();

    [RelayCommand]
    internal async Task RefreshAsync()
    {
        var selected = SelectedGame;
        Games.Clear();
        foreach (var game in runtime.Games) Games.Add(game);
        var next = Games.FirstOrDefault(g => g == selected) ?? runtime.CurrentGame ?? Games.FirstOrDefault();
        if (SelectedGame != next) SelectedGame = next;
        else await LoadReplaysAsync();
    }

    private async Task LoadReplaysAsync()
    {
        var version = ++loadVersion;
        var game = SelectedGame;
        Replays.Clear();
        SelectedReplay = null;
        if (game == null) return;
        try
        {
            var rows = await Task.Run(() => catalog.Value.GetReplaysAsync(game));
            if (version != loadVersion) return;
            foreach (var replay in rows.OrderByDescending(r => r.date))
                Replays.Add(new(replay.id, $"{replay.date:g} · {replay.track} · {replay.car} · {replay.finish_time:F1}s"));
            SelectedReplay = Replays.FirstOrDefault();
            Message = Replays.Count == 0 ? AvaloniaLocalization.Get("replayTelemetry.empty") : string.Empty;
        }
        catch (Exception ex) { if (version == loadVersion) Message = ex.Message; }
    }

    internal async Task ExportAsync(string directory)
    {
        if (IsBusy || SelectedReplay == null) return;
        var id = SelectedReplay.Id;
        IsBusy = true;
        Message = string.Empty;
        try
        {
            var result = await Task.Run(() => catalog.Value.ExportMotecAsync(id, directory));
            Message = AvaloniaLocalization.Format("replay.motecExported", result.LogPath, result.Frequency);
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
}
