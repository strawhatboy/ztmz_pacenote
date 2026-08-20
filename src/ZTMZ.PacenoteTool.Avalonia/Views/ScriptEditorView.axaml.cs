using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using FluentAvalonia.UI.Controls;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.ScriptEditor;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public sealed partial class ScriptEditorView : UserControl, IAsyncDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly NativeWebView _webView;
    private readonly TextBlock _statusText;
    private readonly NumericUpDown _distanceAdjustment;
    private readonly Slider _roadbookScale;
    private readonly TextBlock _roadbookScaleValue;
    private readonly TaskCompletionSource _navigationCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private MonacoAssetServer? _assetServer;
    private AvaloniaMonacoWebViewBridge? _bridge;
    private MonacoEditorController? _controller;
    private Uri? _editorUri;
    private string _currentFile = string.Empty;
    private bool _initialized;
    private bool _initializing;
    private bool _isDirty;
    private bool _loadingScale;

    public ScriptEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        _webView = this.FindControl<NativeWebView>("EditorWebView") ??
            throw new InvalidOperationException("The Monaco WebView was not created.");
        _statusText = this.FindControl<TextBlock>("StatusText") ??
            throw new InvalidOperationException("The editor status text was not created.");
        _distanceAdjustment = this.FindControl<NumericUpDown>("DistanceAdjustment") ??
            throw new InvalidOperationException("The distance adjustment control was not created.");
        _roadbookScale = this.FindControl<Slider>("RoadbookScale") ??
            throw new InvalidOperationException("The roadbook scale control was not created.");
        _roadbookScaleValue = this.FindControl<TextBlock>("RoadbookScaleValue") ??
            throw new InvalidOperationException("The roadbook scale text was not created.");

        _webView.NavigationCompleted += OnNavigationCompleted;
        Loaded += OnLoaded;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        AvaloniaLocalization.CultureChanged += OnCultureChanged;
    }

    public async ValueTask DisposeAsync()
    {
        Loaded -= OnLoaded;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        AvaloniaLocalization.CultureChanged -= OnCultureChanged;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _controller?.Dispose();
        _bridge?.Dispose();
        if (_assetServer != null)
        {
            await _assetServer.DisposeAsync();
        }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs eventArgs)
    {
        if (_initialized || _initializing)
        {
            return;
        }

        _initializing = true;
        try
        {
            var editorAssets = Path.Combine(AppContext.BaseDirectory, "Assets", "Monaco");
            var iconAssets = Path.Combine(AppContext.BaseDirectory, "Assets", "PacenoteIcons");
            _assetServer = new MonacoAssetServer(
                editorAssets,
                Directory.Exists(iconAssets) ? iconAssets : null);
            await _assetServer.StartAsync();

            _bridge = new AvaloniaMonacoWebViewBridge(_webView);
            _controller = new MonacoEditorController(_bridge);
            _controller.ContentChanged += OnEditorContentChanged;
            _editorUri = _assetServer.EditorUri;
            _webView.Source = _editorUri;
            await _navigationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await _controller.CreateAsync();
            await RefreshCatalogAsync();
            await _controller.SetThemeAsync(
                ActualThemeVariant == ThemeVariant.Light
                    ? MonacoEditorTheme.Light
                    : MonacoEditorTheme.Dark);
            _loadingScale = true;
            _roadbookScale.Value = await _controller.GetRoadbookScaleAsync();
            _loadingScale = false;
            UpdateScaleText();
            await _controller.SetRoadbookTitleAsync(
                AvaloniaLocalization.Get("scriptEditor.newDocument"));
            _initialized = true;
            UpdateStatus();
            await _controller.FocusAsync();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Avalonia script editor initialization failed.");
            await ShowErrorAsync(exception.Message);
        }
        finally
        {
            _initializing = false;
            _loadingScale = false;
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs eventArgs)
    {
        if (_editorUri == null || eventArgs.Request != _editorUri)
        {
            return;
        }

        if (eventArgs.IsSuccess)
        {
            _navigationCompleted.TrySetResult();
        }
        else
        {
            _navigationCompleted.TrySetException(
                new InvalidOperationException("Monaco editor assets could not be loaded."));
        }
    }

    private async void OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        UpdateStatus();
        if (_initialized)
        {
            try
            {
                await RefreshCatalogAsync();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Could not refresh the localized Monaco catalog.");
            }
        }
    }

    private async void OnActualThemeVariantChanged(object? sender, EventArgs eventArgs)
    {
        if (!_initialized)
        {
            return;
        }

        await _controller!.SetThemeAsync(
            ActualThemeVariant == ThemeVariant.Light
                ? MonacoEditorTheme.Light
                : MonacoEditorTheme.Dark);
    }

    private void OnEditorContentChanged(object? sender, EventArgs eventArgs)
    {
        _isDirty = true;
        UpdateStatus();
    }

    private async void Open_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (!await EnsureReadyAsync() || !await ConfirmDiscardChangesAsync())
        {
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [PacenoteFileType, FilePickerFileTypes.All]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await OpenFileAsync(path);
    }

    private async void Save_Click(object? sender, RoutedEventArgs eventArgs) => await SaveAsync(false);

    private async void SaveAs_Click(object? sender, RoutedEventArgs eventArgs) => await SaveAsync(true);

    private async void Import_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (!await EnsureReadyAsync() || !await ConfirmDiscardChangesAsync())
        {
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CrewChief JSON") { Patterns = ["*.json"] },
                FilePickerFileTypes.All
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var script = ScriptReader.ReadFromJson(path).ToString();
            await _controller!.SetContentAsync(script);
            _currentFile = string.Empty;
            _isDirty = true;
            await UpdateRoadbookTitleAsync();
            UpdateStatus();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not import CrewChief pacenotes.");
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void Validate_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (await EnsureReadyAsync())
        {
            await _controller!.ValidateAsync();
        }
    }

    private async void PrintRoadbook_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (!await EnsureReadyAsync())
        {
            return;
        }

        try
        {
            await new RoadbookPrintDialog(this).ShowAsync();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Avalonia roadbook print dialog failed.");
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void WordWrap_Changed(object? sender, RoutedEventArgs eventArgs)
    {
        if (_initialized && sender is ToggleSwitch toggle)
        {
            await _controller!.SetWordWrapAsync(toggle.IsChecked == true);
        }
    }

    private async void AdjustDistance_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (!await EnsureReadyAsync())
        {
            return;
        }

        var adjustment = (double)(_distanceAdjustment.Value ?? 0m);
        if (Math.Abs(adjustment) < double.Epsilon)
        {
            return;
        }

        var content = await _controller!.GetContentAsync();
        var updated = Regex.Replace(
            content,
            @"(?m)^(\s*)(-?\d+(?:\.\d+)?)(?=\s*[:,])",
            match => double.TryParse(
                match.Groups[2].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var distance)
                    ? match.Groups[1].Value +
                      (distance + adjustment).ToString("0.###", CultureInfo.InvariantCulture)
                    : match.Value);
        await _controller.SetContentAsync(updated);
        _isDirty = true;
        UpdateStatus();
    }

    private async void RoadbookScale_ValueChanged(
        object? sender,
        global::Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs eventArgs)
    {
        UpdateScaleText();
        if (_initialized && !_loadingScale)
        {
            await _controller!.SetRoadbookScaleAsync(eventArgs.NewValue);
        }
    }

    private async Task OpenFileAsync(string path)
    {
        try
        {
            await _controller!.SetContentAsync(await File.ReadAllTextAsync(path));
            _currentFile = Path.GetFullPath(path);
            _isDirty = false;
            await UpdateRoadbookTitleAsync();
            UpdateStatus();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not open pacenote script {0}.", path);
            await ShowErrorAsync(exception.Message);
        }
    }

    private async Task SaveAsync(bool saveAs)
    {
        if (!await EnsureReadyAsync())
        {
            return;
        }

        var path = _currentFile;
        if (saveAs || string.IsNullOrWhiteSpace(path))
        {
            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider == null)
            {
                return;
            }

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                DefaultExtension = "pacenote",
                FileTypeChoices = [PacenoteFileType],
                SuggestedFileName = string.IsNullOrWhiteSpace(path)
                    ? "roadbook.pacenote"
                    : Path.GetFileName(path)
            });
            path = file?.TryGetLocalPath() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
        }

        try
        {
            await File.WriteAllTextAsync(path, await _controller!.GetContentAsync());
            _currentFile = Path.GetFullPath(path);
            _isDirty = false;
            await UpdateRoadbookTitleAsync();
            UpdateStatus();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not save pacenote script {0}.", path);
            await ShowErrorAsync(exception.Message);
        }
    }

    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!_isDirty)
        {
            return true;
        }

        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = AvaloniaLocalization.Get("scriptEditor.unsavedTitle"),
            Content = AvaloniaLocalization.Get("scriptEditor.unsavedContent"),
            PrimaryButtonText = AvaloniaLocalization.Get("scriptEditor.save"),
            SecondaryButtonText = AvaloniaLocalization.Get("scriptEditor.discard"),
            CloseButtonText = AvaloniaLocalization.Get("dialog.common.btn_cancel"),
            DefaultButton = FAContentDialogButton.Primary
        });
        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this));
        if (result == FAContentDialogResult.Primary)
        {
            await SaveAsync(false);
            return !_isDirty;
        }

        return result == FAContentDialogResult.Secondary;
    }

    private Task<bool> EnsureReadyAsync()
    {
        if (_initialized)
        {
            return Task.FromResult(true);
        }

        return ShowNotReadyAsync();
    }

    private async Task<bool> ShowNotReadyAsync()
    {
        await ShowErrorAsync(AvaloniaLocalization.Get("runtime.initializingRuntime"));
        return false;
    }

    private async Task RefreshCatalogAsync()
    {
        var resolver = _assetServer == null
            ? null
            : new Func<string, string>(_assetServer.ResolveAssetUri);
        await _controller!.ConfigurePacenoteLanguageAsync(
            await PacenoteLanguageCatalogProvider.LoadAsync(resolver));
    }

    private async Task UpdateRoadbookTitleAsync()
    {
        var title = string.IsNullOrWhiteSpace(_currentFile)
            ? AvaloniaLocalization.Get("scriptEditor.newDocument")
            : Path.GetFileNameWithoutExtension(_currentFile);
        await _controller!.SetRoadbookTitleAsync(title);
    }

    private void UpdateStatus()
    {
        var document = string.IsNullOrWhiteSpace(_currentFile)
            ? AvaloniaLocalization.Get("scriptEditor.newDocument")
            : Path.GetFileName(_currentFile);
        _statusText.Text = _isDirty
            ? $"{document} - {AvaloniaLocalization.Get("scriptEditor.unsaved")}"
            : document;
    }

    private void UpdateScaleText() => _roadbookScaleValue.Text = $"{_roadbookScale.Value:0}%";

    internal async Task ExportRoadbookPdfAsync(string fileName, RoadbookPrintOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(options);
        if (!_initialized || _controller == null || _bridge == null)
        {
            throw new InvalidOperationException("The script editor is not ready.");
        }

        await _controller.ApplyPrintPageStyleAsync(options);
        await using var pdfStream = await _bridge.PrintToPdfStreamAsync(new());
        await using var output = new FileStream(
            fileName,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await pdfStream.CopyToAsync(output);
    }

    internal async Task ShowRoadbookPrintUiAsync(RoadbookPrintOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!_initialized || _controller == null || _bridge == null)
        {
            throw new InvalidOperationException("The script editor is not ready.");
        }

        await _controller.ApplyPrintPageStyleAsync(options);
        _bridge.ShowPrintUI();
    }

    internal void SetPrintDialogVisible(bool visible) => _webView.IsVisible = !visible;

    private async Task ShowErrorAsync(string message)
    {
        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = AvaloniaLocalization.Get("scriptEditor.errorTitle"),
            Content = message,
            CloseButtonText = AvaloniaLocalization.Get("dialog.common.btn_ok")
        });
        await dialog.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private static FilePickerFileType PacenoteFileType { get; } = new("Pacenote scripts")
    {
        Patterns = ["*.pacenote"]
    };
}
