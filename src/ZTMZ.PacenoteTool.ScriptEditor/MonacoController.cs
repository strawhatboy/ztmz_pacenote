#nullable enable

using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wpf.Ui.Appearance;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.ScriptEditor;

public sealed class MonacoController : IDisposable
{
    private readonly WebView2 _webView;
    private readonly WebView2MonacoWebViewBridge _bridge;
    private readonly MonacoEditorController _controller;

    public MonacoController(WebView2 webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _bridge = new WebView2MonacoWebViewBridge(webView);
        _controller = new MonacoEditorController(_bridge);
        _controller.ContentChanged += OnContentChanged;
    }

    public event EventHandler? ContentChanged;

    public Task CreateAsync() => _controller.CreateAsync();

    public Task RegisterPacenoteLanguageAsync() => Task.CompletedTask;

    public Task ConfigurePacenoteLanguageAsync(PacenoteLanguageCatalog catalog) =>
        _controller.ConfigurePacenoteLanguageAsync(catalog);

    public Task SetThemeAsync(ApplicationTheme applicationTheme) => _controller.SetThemeAsync(
        applicationTheme == ApplicationTheme.Light
            ? MonacoEditorTheme.Light
            : MonacoEditorTheme.Dark);

    public Task SetLanguageAsync(MonacoLanguage monacoLanguage) => Task.CompletedTask;

    public Task SetContentAsync(string contents) => _controller.SetContentAsync(contents);

    public Task<string> GetContentAsync() => _controller.GetContentAsync();

    public Task ValidateAsync() => _controller.ValidateAsync();

    public Task SetWordWrapAsync(bool enabled) => _controller.SetWordWrapAsync(enabled);

    public Task SetReadOnlyAsync(bool readOnly) => _controller.SetReadOnlyAsync(readOnly);

    public Task SetRoadbookTitleAsync(string title) => _controller.SetRoadbookTitleAsync(title);

    public Task SetRoadbookScaleAsync(double percent) => _controller.SetRoadbookScaleAsync(percent);

    public Task<double> GetRoadbookScaleAsync() => _controller.GetRoadbookScaleAsync();

    public async Task<bool> ExportRoadbookPdfAsync(string fileName, RoadbookPrintOptions options)
    {
        await _controller.ApplyPrintPageStyleAsync(options);
        return await _webView.CoreWebView2.PrintToPdfAsync(fileName, CreatePrintSettings(options));
    }

    public async Task<RoadbookPrintResult> PrintRoadbookAsync(RoadbookPrintOptions options)
    {
        await _controller.ApplyPrintPageStyleAsync(options);
        var status = await _webView.CoreWebView2.PrintAsync(CreatePrintSettings(options));
        return status switch
        {
            CoreWebView2PrintStatus.Succeeded => RoadbookPrintResult.Succeeded,
            CoreWebView2PrintStatus.PrinterUnavailable => RoadbookPrintResult.PrinterUnavailable,
            _ => RoadbookPrintResult.OtherError
        };
    }

    public Task HighlightPlaybackAsync(PacenotePlaybackSnapshot snapshot) =>
        _controller.HighlightPlaybackAsync(snapshot);

    public Task FocusAsync() => _controller.FocusAsync();

    public void Dispose()
    {
        _controller.ContentChanged -= OnContentChanged;
        _controller.Dispose();
        _bridge.Dispose();
    }

    private CoreWebView2PrintSettings CreatePrintSettings(RoadbookPrintOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = _webView.CoreWebView2.Environment.CreatePrintSettings();
        settings.Orientation = options.Landscape
            ? CoreWebView2PrintOrientation.Landscape
            : CoreWebView2PrintOrientation.Portrait;
        settings.MediaSize = CoreWebView2PrintMediaSize.Custom;
        settings.PageWidth = options.PaperWidthMillimeters / 25.4;
        settings.PageHeight = options.PaperHeightMillimeters / 25.4;
        settings.MarginTop = options.VerticalMarginMillimeters / 25.4;
        settings.MarginBottom = options.VerticalMarginMillimeters / 25.4;
        settings.MarginLeft = options.HorizontalMarginMillimeters / 25.4;
        settings.MarginRight = options.HorizontalMarginMillimeters / 25.4;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.PageRanges = options.PageRanges?.Trim() ?? string.Empty;
        settings.Copies = Math.Clamp(options.Copies, 1, 999);
        settings.PrinterName = options.PrinterName ?? string.Empty;
        return settings;
    }

    private static string BuildPrintPageStyle(RoadbookPrintOptions options) =>
        RoadbookPrintCssBuilder.BuildPageStyle(options);

    private void OnContentChanged(object? sender, EventArgs eventArgs)
    {
        if (Application.Current?.Dispatcher.CheckAccess() != false)
        {
            ContentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _ = Application.Current.Dispatcher.InvokeAsync(
            () => ContentChanged?.Invoke(this, EventArgs.Empty));
    }
}

internal sealed class WebView2MonacoWebViewBridge : IMonacoWebViewBridge, IDisposable
{
    private readonly WebView2 _webView;
    private CoreWebView2? _coreWebView;
    private bool _disposed;

    public WebView2MonacoWebViewBridge(WebView2 webView)
    {
        _webView = webView;
    }

    public event EventHandler<MonacoWebMessageReceivedEventArgs>? WebMessageReceived;

    public async Task<string?> InvokeScriptAsync(string script)
    {
        EnsureMessageHandlerAttached();
        return await _webView.ExecuteScriptAsync(script);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_coreWebView != null)
        {
            _coreWebView.WebMessageReceived -= OnWebMessageReceived;
            _coreWebView = null;
        }
        _disposed = true;
    }

    private void EnsureMessageHandlerAttached()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var coreWebView = _webView.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 must be initialized before invoking Monaco.");
        if (ReferenceEquals(coreWebView, _coreWebView))
        {
            return;
        }

        if (_coreWebView != null)
        {
            _coreWebView.WebMessageReceived -= OnWebMessageReceived;
        }

        _coreWebView = coreWebView;
        _coreWebView.WebMessageReceived += OnWebMessageReceived;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs) =>
        WebMessageReceived?.Invoke(
            this,
            new MonacoWebMessageReceivedEventArgs(eventArgs.WebMessageAsJson));
}
