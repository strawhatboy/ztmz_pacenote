namespace ZTMZ.PacenoteTool.ScriptEditor;

public interface IMonacoWebViewBridge
{
    event EventHandler<MonacoWebMessageReceivedEventArgs>? WebMessageReceived;

    Task<string?> InvokeScriptAsync(string script);
}

public sealed class MonacoWebMessageReceivedEventArgs(string body) : EventArgs
{
    public string Body { get; } = body;
}
