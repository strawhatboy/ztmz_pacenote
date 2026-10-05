using System;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.ViewModels.Dialog;

public partial class StartupDialogVM : ObservableObject, IDisposable
{
    private readonly ZTMZPacenoteTool _tool;
    private bool _disposed;

    public StartupDialogVM(ZTMZPacenoteTool tool)
    {
        _tool = tool;
        _tool.onStatusReport += OnStatusReport;
    }

    private void OnStatusReport(string status)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed)
                Status = status;
        }));
    }

    public void Dispose()
    {
        _disposed = true;
        _tool.onStatusReport -= OnStatusReport;
    }

    [ObservableProperty]
    private string _status = "Loading...";
}
