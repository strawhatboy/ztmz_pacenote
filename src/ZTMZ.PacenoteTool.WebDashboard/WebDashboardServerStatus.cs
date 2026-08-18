namespace ZTMZ.PacenoteTool.WebDashboard;

public enum WebDashboardServerState
{
    Stopped,
    Running,
    PortUnavailable,
    Error
}

public sealed class WebDashboardServerStatus
{
    public WebDashboardServerStatus(
        WebDashboardServerState state,
        int port,
        string url,
        string errorMessage = "")
    {
        State = state;
        Port = port;
        Url = url;
        ErrorMessage = errorMessage;
    }

    public WebDashboardServerState State { get; }

    public int Port { get; }

    public string Url { get; }

    public string ErrorMessage { get; }

    public bool IsRunning => State == WebDashboardServerState.Running;
}
