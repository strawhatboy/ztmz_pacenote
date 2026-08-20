#nullable enable

using System;
using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base;

/// <summary>
/// A platform-neutral entry in the application's notification-area menu.
/// </summary>
public sealed record TrayMenuEntry(
    string Header,
    bool IsEnabled = true,
    bool IsChecked = false,
    TrayMenuToggleType ToggleType = TrayMenuToggleType.None,
    string? ToolTip = null,
    Action? Activate = null,
    IReadOnlyList<TrayMenuEntry>? Children = null)
{
    public bool IsSeparator { get; init; }

    public static TrayMenuEntry Separator() => new(string.Empty) { IsSeparator = true };
}

public enum TrayMenuToggleType
{
    None,
    CheckBox,
    Radio
}
