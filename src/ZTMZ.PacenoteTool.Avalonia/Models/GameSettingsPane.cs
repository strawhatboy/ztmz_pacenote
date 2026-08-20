#nullable enable

using System;
using Avalonia.Controls;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Avalonia.Models;

/// <summary>
/// Abstract base class for game-specific settings panes, analogous to
/// ZTMZ.PacenoteTool.UI.Windows.Game.IGameConfigSettingsPane (WPF).
/// </summary>
public abstract class GameSettingsPane : UserControl, IDisposable
{
    public abstract void InitializeWithGame(IGame game);
    public virtual void RefreshFromConfiguration() { }

    public event Action? RestartNeeded;

    protected void RaiseRestartNeeded() => RestartNeeded?.Invoke();

    public virtual void Dispose()
    {
    }
}
