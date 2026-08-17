using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wpf.Ui.Appearance;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.ScriptEditor;

public sealed class MonacoController
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebView2 _webView;
    private bool _messageHandlerAttached;

    public MonacoController(WebView2 webView)
    {
        _webView = webView;
    }

    public event EventHandler? ContentChanged;

    public async Task CreateAsync()
    {
        AttachMessageHandler();
        await _webView.ExecuteScriptAsync(
            "window.ztmzEditor.create(document.querySelector('#root'));"
        );
    }

    public Task RegisterPacenoteLanguageAsync()
    {
        return Task.CompletedTask;
    }

    public async Task ConfigurePacenoteLanguageAsync(PacenoteLanguageCatalog catalog)
    {
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "catalog",
            catalog
        }, JsonOptions));
        await _webView.ExecuteScriptAsync("window.ztmzEditor.validate();");
    }

    public async Task SetThemeAsync(ApplicationTheme applicationTheme)
    {
        var theme = applicationTheme == ApplicationTheme.Light ? "vs" : "vs-dark";
        await _webView.ExecuteScriptAsync($"window.ztmzEditor.setTheme('{theme}');");
    }

    public async Task SetLanguageAsync(MonacoLanguage monacoLanguage)
    {
        await _webView.ExecuteScriptAsync("window.ztmzEditor.setLanguage('pacenote');");
    }

    public async Task SetContentAsync(string contents)
    {
        await _webView.ExecuteScriptAsync(
            $"window.ztmzEditor.setValue({JsonSerializer.Serialize(contents ?? string.Empty, JsonOptions)});"
        );
    }

    public async Task<string> GetContentAsync()
    {
        var value = await _webView.ExecuteScriptAsync("JSON.stringify(window.ztmzEditor.getValue());");
        return JsonSerializer.Deserialize<string>(value) ?? string.Empty;
    }

    public Task ValidateAsync()
    {
        return ExecuteAsync("window.ztmzEditor.validate();");
    }

    public Task SetWordWrapAsync(bool enabled)
    {
        return ExecuteAsync($"window.ztmzEditor.setWordWrap({enabled.ToString().ToLowerInvariant()});");
    }

    public Task SetReadOnlyAsync(bool readOnly)
    {
        return ExecuteAsync($"window.ztmzEditor.setReadOnly({readOnly.ToString().ToLowerInvariant()});");
    }

    public Task SetRoadbookTitleAsync(string title)
    {
        return ExecuteAsync($"window.ztmzEditor.setRoadbookTitle({JsonSerializer.Serialize(title ?? string.Empty, JsonOptions)});");
    }

    public Task SetRoadbookScaleAsync(double percent)
    {
        return ExecuteAsync($"window.ztmzEditor.setRoadbookScale({percent.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
    }

    public async Task<double> GetRoadbookScaleAsync()
    {
        var value = await _webView.ExecuteScriptAsync("window.ztmzEditor.getRoadbookScale();");
        return JsonSerializer.Deserialize<double>(value);
    }

    public async Task<bool> ExportRoadbookPdfAsync(string fileName, RoadbookPrintOptions options)
    {
        await ApplyPrintPageStyleAsync(options);
        return await _webView.CoreWebView2.PrintToPdfAsync(fileName, CreatePrintSettings(options));
    }

    public async Task<RoadbookPrintResult> PrintRoadbookAsync(RoadbookPrintOptions options)
    {
        await ApplyPrintPageStyleAsync(options);
        var status = await _webView.CoreWebView2.PrintAsync(CreatePrintSettings(options));
        return status switch
        {
            CoreWebView2PrintStatus.Succeeded => RoadbookPrintResult.Succeeded,
            CoreWebView2PrintStatus.PrinterUnavailable => RoadbookPrintResult.PrinterUnavailable,
            _ => RoadbookPrintResult.OtherError
        };
    }

    public Task HighlightPlaybackAsync(PacenotePlaybackSnapshot snapshot)
    {
        return ExecuteAsync($"window.ztmzEditor.highlightPlayback({JsonSerializer.Serialize(snapshot, JsonOptions)});");
    }

    public Task FocusAsync()
    {
        return ExecuteAsync("window.ztmzEditor.focus();");
    }

    private Task ExecuteAsync(string script)
    {
        return _webView.ExecuteScriptAsync(script);
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

    private Task ApplyPrintPageStyleAsync(RoadbookPrintOptions options)
    {
        var style = BuildPrintPageStyle(options);
        return ExecuteAsync(
            $"window.ztmzEditor.setPrintPageStyle({JsonSerializer.Serialize(style, JsonOptions)});"
        );
    }

    private static string BuildPrintPageStyle(RoadbookPrintOptions options)
    {
        if (!options.ShowPageNumbers)
        {
            return string.Empty;
        }

        var marginBox = options.PageNumberPosition switch
        {
            RoadbookPageNumberPosition.BottomLeft => "@bottom-left",
            RoadbookPageNumberPosition.BottomRight => "@bottom-right",
            _ => "@bottom-center"
        };
        var content = options.PageNumberStyle == RoadbookPageNumberStyle.Current
            ? "counter(page)"
            : "counter(page) ' / ' counter(pages)";
        return $"@page {{ {marginBox} {{ content: {content}; color: #5f6368; font-family: sans-serif; font-size: 9pt; }} }}";
    }

    private void AttachMessageHandler()
    {
        if (_messageHandlerAttached)
        {
            return;
        }

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _messageHandlerAttached = true;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            if (message.RootElement.TryGetProperty("type", out var type) &&
                type.GetString() == "changed")
            {
                Application.Current.Dispatcher.InvokeAsync(() => ContentChanged?.Invoke(this, EventArgs.Empty));
            }
        }
        catch (JsonException)
        {
            // Ignore messages not sent by the local editor bridge.
        }
    }
}

public sealed record RoadbookPrintOptions(
    double PaperWidthMillimeters,
    double PaperHeightMillimeters,
    bool Landscape,
    double HorizontalMarginMillimeters,
    double VerticalMarginMillimeters,
    string PageRanges = "",
    int Copies = 1,
    string PrinterName = "",
    bool ShowPageNumbers = true,
    RoadbookPageNumberStyle PageNumberStyle = RoadbookPageNumberStyle.CurrentAndTotal,
    RoadbookPageNumberPosition PageNumberPosition = RoadbookPageNumberPosition.BottomCenter);

public enum RoadbookPageNumberStyle
{
    CurrentAndTotal,
    Current
}

public enum RoadbookPageNumberPosition
{
    BottomLeft,
    BottomCenter,
    BottomRight
}

public enum RoadbookPrintResult
{
    Succeeded,
    PrinterUnavailable,
    OtherError
}
