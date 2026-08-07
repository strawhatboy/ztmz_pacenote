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

    public async Task<bool> ExportRoadbookPdfAsync(string fileName)
    {
        var printSettings = _webView.CoreWebView2.Environment.CreatePrintSettings();
        printSettings.Orientation = CoreWebView2PrintOrientation.Portrait;
        printSettings.ShouldPrintBackgrounds = true;
        printSettings.ShouldPrintHeaderAndFooter = false;
        return await _webView.CoreWebView2.PrintToPdfAsync(fileName, printSettings);
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
