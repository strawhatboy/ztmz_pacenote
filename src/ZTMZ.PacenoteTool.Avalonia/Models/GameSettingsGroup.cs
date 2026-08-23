using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Avalonia.Models;

public sealed partial class GameSettingsGroup : ObservableObject, IDisposable
{
    public GameSettingsGroup(IGame game, IReadOnlyList<GameSettingsPane> panes, Bitmap? image)
    {
        Game = game;
        Name = game.Name;
        Description = game.Description;
        Panes = panes;
        Image = image;
    }

    public IGame Game { get; }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<GameSettingsPane> Panes { get; }

    public Bitmap? Image { get; }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private IBrush? _connectionBrush;

    public void UpdateRuntimeState(
        bool isRunning,
        bool isInitialized,
        string connectedText,
        string failedText,
        IBrush connectedBrush,
        IBrush failedBrush)
    {
        IsRunning = isRunning;
        ConnectionStatus = isInitialized ? connectedText : failedText;
        ConnectionBrush = isInitialized ? connectedBrush : failedBrush;
    }

    public void Dispose()
    {
        Image?.Dispose();
    }
}
