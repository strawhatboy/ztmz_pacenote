using Avalonia;
using Avalonia.Controls;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

/// <summary>
/// Uses Avalonia's desktop tray integration on every supported desktop platform.
/// </summary>
internal sealed class AvaloniaTrayService : IDisposable
{
    private readonly NativeMenu _menu = new();
    private readonly TrayIcon _trayIcon;

    public AvaloniaTrayService(string iconPath)
    {
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconPath),
            Menu = _menu,
            IsVisible = true
        };
        _trayIcon.Clicked += OnTrayIconClicked;

        var application = Application.Current ??
            throw new InvalidOperationException("Avalonia application has not been created.");
        TrayIcon.SetIcons(application, new TrayIcons { _trayIcon });
    }

    public event EventHandler? ShowRequested;

    /// <summary>
    /// Gives a platform notification backend access to the native icon already registered by Avalonia.
    /// </summary>
    public object? GetPlatformImplementation() => _trayIcon.GetType()
        .GetField("_impl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?.GetValue(_trayIcon);

    public void UpdateMenu(string title, IReadOnlyList<TrayMenuEntry> entries)
    {
        _trayIcon.ToolTipText = title;
        _menu.Items.Clear();
        foreach (var entry in entries)
        {
            _menu.Items.Add(CreateMenuItem(entry));
        }
    }

    public void Dispose()
    {
        _trayIcon.Clicked -= OnTrayIconClicked;
        _trayIcon.Dispose();
    }

    private void OnTrayIconClicked(object? sender, EventArgs eventArgs) =>
        ShowRequested?.Invoke(this, EventArgs.Empty);

    private static NativeMenuItemBase CreateMenuItem(TrayMenuEntry entry)
    {
        if (entry.IsSeparator)
        {
            return new NativeMenuItemSeparator();
        }

        var item = new NativeMenuItem
        {
            Header = entry.Header,
            IsEnabled = entry.IsEnabled,
            IsChecked = entry.IsChecked,
            ToggleType = entry.ToggleType switch
            {
                TrayMenuToggleType.CheckBox => MenuItemToggleType.CheckBox,
                TrayMenuToggleType.Radio => MenuItemToggleType.Radio,
                _ => MenuItemToggleType.None
            },
            ToolTip = entry.ToolTip
        };

        if (entry.Children is { Count: > 0 })
        {
            var children = new NativeMenu();
            foreach (var child in entry.Children)
            {
                children.Items.Add(CreateMenuItem(child));
            }

            item.Menu = children;
        }
        else if (entry.Activate != null)
        {
            item.Click += (_, _) => entry.Activate();
        }

        return item;
    }
}
