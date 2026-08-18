#if WINDOWS
using DrawingIcon = System.Drawing.Icon;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal sealed class WindowsTrayService : IDisposable
{
    private readonly FormsNotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _showItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _exitItem;

    public WindowsTrayService(string iconPath)
    {
        using var sourceIcon = new DrawingIcon(iconPath);
        _notifyIcon = new FormsNotifyIcon
        {
            Icon = (DrawingIcon)sourceIcon.Clone(),
            Visible = true
        };

        var menu = new FormsContextMenuStrip();
        _showItem = new System.Windows.Forms.ToolStripMenuItem();
        _exitItem = new System.Windows.Forms.ToolStripMenuItem();
        _showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_showItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_exitItem);
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public void UpdateText(string title, string showText, string exitText)
    {
        _notifyIcon.Text = title.Length > 63 ? title[..63] : title;
        _showItem.Text = showText;
        _exitItem.Text = exitText;
    }

    public void ShowNotification(string title, string message) =>
        _notifyIcon.ShowBalloonTip(5000, title, message, FormsToolTipIcon.Info);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }
}
#endif
