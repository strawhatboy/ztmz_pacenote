using Avalonia.Controls;
using Avalonia.Platform;
using ZTMZ.PacenoteTool.ScriptEditor;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal sealed class AvaloniaMonacoWebViewBridge : IMonacoWebViewBridge, IDisposable
{
    private readonly NativeWebView _webView;
    private bool _disposed;

    public AvaloniaMonacoWebViewBridge(NativeWebView webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _webView.WebMessageReceived += OnWebMessageReceived;
    }

    public event EventHandler<MonacoWebMessageReceivedEventArgs>? WebMessageReceived;

    public Task<string?> InvokeScriptAsync(string script) => _webView.InvokeScript(script);

    public Task<Stream> PrintToPdfStreamAsync(WebViewPrintSettings settings) =>
        _webView.PrintToPdfStreamAsync(settings);

    public void ShowPrintUI() => _webView.ShowPrintUI();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _webView.WebMessageReceived -= OnWebMessageReceived;
        _disposed = true;
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs eventArgs) =>
        WebMessageReceived?.Invoke(
            this,
            new MonacoWebMessageReceivedEventArgs(eventArgs.Body ?? string.Empty));
}
