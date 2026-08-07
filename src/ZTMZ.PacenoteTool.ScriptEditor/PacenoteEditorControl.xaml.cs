using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Appearance;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.ScriptEditor;

public partial class PacenoteEditorControl : UserControl
{
    private MonacoController? _controller;
    private string _pendingText = string.Empty;
    private string _pendingRoadbookTitle = string.Empty;
    private bool _pendingReadOnly;
    private bool _isReady;
    private bool _initializing;

    public PacenoteEditorControl()
    {
        InitializeComponent();
    }

    public bool IsReady => _isReady;

    public event EventHandler? ContentChanged;

    public async Task InitializeAsync()
    {
        if (_isReady || _initializing)
        {
            return;
        }

        _initializing = true;
        try
        {
            await EditorWebView.EnsureCoreWebView2Async();
            EditorWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            var navigationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EditorWebView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationFinished.TrySetResult();
                }
                else
                {
                    navigationFinished.TrySetException(new InvalidOperationException("Monaco editor assets could not be loaded."));
                }
            };
            EditorWebView.Source = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Monaco", "index.html"));
            await navigationFinished.Task;

            _controller = new MonacoController(EditorWebView);
            _controller.ContentChanged += (_, _) => ContentChanged?.Invoke(this, EventArgs.Empty);
            await _controller.CreateAsync();
            await _controller.ConfigurePacenoteLanguageAsync(await PacenoteLanguageCatalogProvider.LoadAsync());
            await _controller.SetThemeAsync(ApplicationThemeManager.GetAppTheme());
            await _controller.SetContentAsync(_pendingText);
            await _controller.SetReadOnlyAsync(_pendingReadOnly);
            await _controller.SetRoadbookTitleAsync(_pendingRoadbookTitle);
            _isReady = true;
        }
        finally
        {
            _initializing = false;
        }
    }

    public async Task SetTextAsync(string text)
    {
        _pendingText = text ?? string.Empty;
        if (_controller != null)
        {
            await _controller.SetContentAsync(_pendingText);
        }
    }

    public async Task<string> GetTextAsync()
    {
        return _controller == null ? _pendingText : await _controller.GetContentAsync();
    }

    public Task ValidateAsync()
    {
        return _controller?.ValidateAsync() ?? Task.CompletedTask;
    }

    public Task SetWordWrapAsync(bool enabled)
    {
        return _controller?.SetWordWrapAsync(enabled) ?? Task.CompletedTask;
    }

    public Task SetReadOnlyAsync(bool readOnly)
    {
        _pendingReadOnly = readOnly;
        return _controller?.SetReadOnlyAsync(readOnly) ?? Task.CompletedTask;
    }

    public Task SetRoadbookTitleAsync(string title)
    {
        _pendingRoadbookTitle = title ?? string.Empty;
        return _controller?.SetRoadbookTitleAsync(_pendingRoadbookTitle) ?? Task.CompletedTask;
    }

    public Task<bool> ExportRoadbookPdfAsync(string fileName)
    {
        return _controller?.ExportRoadbookPdfAsync(fileName) ?? Task.FromResult(false);
    }

    public Task HighlightPlaybackAsync(PacenotePlaybackSnapshot snapshot)
    {
        return _controller?.HighlightPlaybackAsync(snapshot) ?? Task.CompletedTask;
    }

    public Task FocusEditorAsync()
    {
        return _controller?.FocusAsync() ?? Task.CompletedTask;
    }
}
