using System.Globalization;
using System.Text.Json;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.ScriptEditor;

public sealed class MonacoEditorController : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IMonacoWebViewBridge _bridge;
    private bool _disposed;

    public MonacoEditorController(IMonacoWebViewBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _bridge.WebMessageReceived += OnWebMessageReceived;
    }

    public event EventHandler? ContentChanged;

    public Task CreateAsync() => ExecuteAsync(
        "window.ztmzEditor.create(document.querySelector('#root'));");

    public Task ConfigurePacenoteLanguageAsync(PacenoteLanguageCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var message = JsonSerializer.Serialize(new { type = "catalog", catalog }, JsonOptions);
        return ExecuteAsync($"window.ztmzHost.receive({message}); window.ztmzEditor.validate();");
    }

    public Task SetThemeAsync(MonacoEditorTheme theme)
    {
        var name = theme == MonacoEditorTheme.Light ? "vs" : "vs-dark";
        return ExecuteAsync($"window.ztmzEditor.setTheme('{name}');");
    }

    public Task SetContentAsync(string? contents) => ExecuteAsync(
        $"window.ztmzEditor.setValue({JsonSerializer.Serialize(contents ?? string.Empty, JsonOptions)});");

    public async Task<string> GetContentAsync()
    {
        var value = await _bridge.InvokeScriptAsync("window.ztmzEditor.getValue();");
        return ParseScriptString(value);
    }

    public Task ValidateAsync() => ExecuteAsync("window.ztmzEditor.validate();");

    public Task SetWordWrapAsync(bool enabled) => ExecuteAsync(
        $"window.ztmzEditor.setWordWrap({enabled.ToString().ToLowerInvariant()});");

    public Task SetReadOnlyAsync(bool readOnly) => ExecuteAsync(
        $"window.ztmzEditor.setReadOnly({readOnly.ToString().ToLowerInvariant()});");

    public Task SetRoadbookTitleAsync(string? title) => ExecuteAsync(
        $"window.ztmzEditor.setRoadbookTitle({JsonSerializer.Serialize(title ?? string.Empty, JsonOptions)});");

    public Task SetRoadbookScaleAsync(double percent) => ExecuteAsync(
        $"window.ztmzEditor.setRoadbookScale({percent.ToString(CultureInfo.InvariantCulture)});");

    public async Task<double> GetRoadbookScaleAsync()
    {
        var value = await _bridge.InvokeScriptAsync("window.ztmzEditor.getRoadbookScale();");
        return TryParseScriptDouble(value, out var scale) ? scale : 100d;
    }

    public Task ApplyPrintPageStyleAsync(RoadbookPrintOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var style = RoadbookPrintCssBuilder.BuildPageStyle(options);
        return ExecuteAsync(
            $"window.ztmzEditor.setPrintPageStyle({JsonSerializer.Serialize(style, JsonOptions)});");
    }

    public Task HighlightPlaybackAsync(PacenotePlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ExecuteAsync(
            $"window.ztmzEditor.highlightPlayback({JsonSerializer.Serialize(snapshot, JsonOptions)});");
    }

    public Task FocusAsync() => ExecuteAsync("window.ztmzEditor.focus();");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _bridge.WebMessageReceived -= OnWebMessageReceived;
        _disposed = true;
    }

    private async Task ExecuteAsync(string script)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _bridge.InvokeScriptAsync(script);
    }

    private void OnWebMessageReceived(object? sender, MonacoWebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            using var message = JsonDocument.Parse(eventArgs.Body);
            if (message.RootElement.TryGetProperty("type", out var type) &&
                type.GetString() == "changed")
            {
                ContentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (JsonException)
        {
            // Ignore messages not sent by the local Monaco bridge.
        }
    }

    private static string ParseScriptString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(value) ?? value;
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static bool TryParseScriptDouble(string? value, out double result)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        try
        {
            result = JsonSerializer.Deserialize<double>(value ?? string.Empty);
            return true;
        }
        catch (JsonException)
        {
            result = default;
            return false;
        }
    }
}

public enum MonacoEditorTheme
{
    Light,
    Dark
}
