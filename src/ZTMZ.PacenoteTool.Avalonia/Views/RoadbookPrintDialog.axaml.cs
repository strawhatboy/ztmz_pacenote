using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.ScriptEditor;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

internal sealed partial class RoadbookPrintDialog : UserControl, IAsyncDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly ScriptEditorView _editor;
    private readonly NativeWebView _previewWebView;
    private readonly Border _previewLoading;
    private readonly TextBlock _previewStatusText;
    private readonly ComboBox _paperBox;
    private readonly ComboBox _orientationBox;
    private readonly NumericUpDown _horizontalMarginBox;
    private readonly NumericUpDown _verticalMarginBox;
    private readonly ToggleSwitch _pageNumbersToggle;
    private readonly ComboBox _pageNumberStyleBox;
    private readonly ComboBox _pageNumberPositionBox;
    private readonly DispatcherTimer _previewTimer;
    private readonly SemaphoreSlim _printLock = new(1, 1);
    private readonly string _previewDirectory = Path.Combine(
        Path.GetTempPath(),
        "ZTMZ-roadbook-print-preview",
        Guid.NewGuid().ToString("N"));
    private FAContentDialog? _dialog;
    private bool _opened;
    private bool _disposed;
    private bool _isBusy;
    private int _previewSequence;
    private Uri? _activePreviewUri;
    private RoadbookPrintOptions? _pendingSystemPrint;

    public RoadbookPrintDialog(ScriptEditorView editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        AvaloniaXamlLoader.Load(this);
        _previewWebView = Find<NativeWebView>("PreviewWebView");
        _previewLoading = Find<Border>("PreviewLoading");
        _previewStatusText = Find<TextBlock>("PreviewStatusText");
        _paperBox = Find<ComboBox>("PaperBox");
        _orientationBox = Find<ComboBox>("OrientationBox");
        _horizontalMarginBox = Find<NumericUpDown>("HorizontalMarginBox");
        _verticalMarginBox = Find<NumericUpDown>("VerticalMarginBox");
        _pageNumbersToggle = Find<ToggleSwitch>("PageNumbersToggle");
        _pageNumberStyleBox = Find<ComboBox>("PageNumberStyleBox");
        _pageNumberPositionBox = Find<ComboBox>("PageNumberPositionBox");

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _previewTimer.Tick += PreviewTimer_Tick;
        _previewWebView.NavigationCompleted += PreviewWebView_NavigationCompleted;
        InitializeText();
        InitializeChoices();
    }

    public async Task ShowAsync()
    {
        var owner = TopLevel.GetTopLevel(_editor) ??
            throw new InvalidOperationException("The print dialog has no window owner.");
        var dialogWidth = Math.Min(1100d, Math.Max(680d, owner.Bounds.Width - 48d));
        var dialogHeight = Math.Min(720d, Math.Max(500d, owner.Bounds.Height - 48d));
        _dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = Text("scriptEditor.printDialog.title"),
            Content = this,
            Width = dialogWidth,
            Height = dialogHeight,
            MaxWidth = dialogWidth,
            MaxHeight = dialogHeight,
            PrimaryButtonText = Text("scriptEditor.printDialog.export"),
            SecondaryButtonText = Text("scriptEditor.printDialog.print"),
            CloseButtonText = Text("scriptEditor.printDialog.cancel"),
            DefaultButton = FAContentDialogButton.Primary
        });
        // FluentAvalonia's default ContentDialog template caps its inner border at 548px.
        // This dialog contains a PDF preview and a full settings column, so override that
        // template resource locally instead of changing the application's normal dialogs.
        _dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        _dialog.Resources["ContentDialogMaxHeight"] = dialogHeight;
        _dialog.Opened += Dialog_Opened;
        _dialog.PrimaryButtonClick += ExportPdf_Click;
        _dialog.SecondaryButtonClick += Print_Click;

        _editor.SetPrintDialogVisible(true);
        try
        {
            await _dialog.ShowAsync(owner);
        }
        finally
        {
            _editor.SetPrintDialogVisible(false);
            await DisposeAsync();
            if (_pendingSystemPrint is { } printOptions)
            {
                Logger.Info("Roadbook print settings dialog closed; scheduling the system print UI.");
                // WebView2 defers its browser print UI behind an active ContentDialog. Queue
                // it after this dialog has detached and the editor native view is visible again.
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        Logger.Info("Opening the system roadbook print UI.");
                        await _editor.ShowRoadbookPrintUiAsync(printOptions);
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception, "Could not open the system roadbook print UI.");
                    }
                }, DispatcherPriority.Background);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _previewTimer.Stop();
        _previewTimer.Tick -= PreviewTimer_Tick;
        _previewWebView.NavigationCompleted -= PreviewWebView_NavigationCompleted;
        await _printLock.WaitAsync();
        try
        {
            foreach (var file in Directory.Exists(_previewDirectory)
                         ? Directory.EnumerateFiles(_previewDirectory, "*.pdf")
                         : [])
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            try
            {
                if (Directory.Exists(_previewDirectory))
                {
                    Directory.Delete(_previewDirectory, false);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        finally
        {
            _printLock.Release();
            _printLock.Dispose();
        }
    }

    private void Dialog_Opened(object? sender, EventArgs eventArgs)
    {
        _opened = true;
        Directory.CreateDirectory(_previewDirectory);
        SchedulePreview(TimeSpan.Zero);
    }

    private void InitializeText()
    {
        Find<TextBlock>("LayoutHeading").Text = Text("scriptEditor.printDialog.layout");
        Find<TextBlock>("PaperLabel").Text = Text("scriptEditor.printDialog.paper");
        Find<TextBlock>("OrientationLabel").Text = Text("scriptEditor.printDialog.orientation");
        Find<TextBlock>("MarginsLabel").Text = Text("scriptEditor.printDialog.margins");
        Find<TextBlock>("HorizontalMarginLabel").Text = Text("scriptEditor.printDialog.horizontal");
        Find<TextBlock>("VerticalMarginLabel").Text = Text("scriptEditor.printDialog.vertical");
        Find<TextBlock>("PageNumbersLabel").Text = Text("scriptEditor.printDialog.pageNumbers");
        _previewStatusText.Text = Text("scriptEditor.printDialog.preparingPreview");
    }

    private void InitializeChoices()
    {
        _paperBox.ItemsSource = new[] { "A3", "A4", "A5", "Letter", "Legal" };
        _paperBox.SelectedIndex = 1;
        _orientationBox.ItemsSource = new[]
        {
            Text("scriptEditor.printDialog.portrait"),
            Text("scriptEditor.printDialog.landscape")
        };
        _orientationBox.SelectedIndex = 0;
        _pageNumberStyleBox.ItemsSource = new[] { "1 / N", "1" };
        _pageNumberStyleBox.SelectedIndex = 0;
        _pageNumberPositionBox.ItemsSource = new[]
        {
            Text("scriptEditor.printDialog.pageNumberBottomLeft"),
            Text("scriptEditor.printDialog.pageNumberBottomCenter"),
            Text("scriptEditor.printDialog.pageNumberBottomRight")
        };
        _pageNumberPositionBox.SelectedIndex = 1;
        UpdatePageNumberControls();
    }

    private void PreviewSetting_Changed(object? sender, SelectionChangedEventArgs eventArgs) => SchedulePreview();

    private void PreviewNumberSetting_Changed(
        object? sender,
        NumericUpDownValueChangedEventArgs eventArgs) => SchedulePreview();

    private void PageNumbers_Changed(object? sender, RoutedEventArgs eventArgs)
    {
        UpdatePageNumberControls();
        SchedulePreview();
    }

    private void UpdatePageNumberControls()
    {
        var enabled = _pageNumbersToggle.IsChecked == true;
        _pageNumberStyleBox.IsEnabled = enabled;
        _pageNumberPositionBox.IsEnabled = enabled;
    }

    private void SchedulePreview(TimeSpan? delay = null)
    {
        if (!_opened || _disposed || _isBusy)
        {
            return;
        }

        _previewTimer.Stop();
        _previewTimer.Interval = delay ?? TimeSpan.FromMilliseconds(300);
        _previewTimer.Start();
    }

    private async void PreviewTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _previewTimer.Stop();
        await RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _printLock.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            SetBusy(true, Text("scriptEditor.printDialog.preparingPreview"));
            var previewPath = Path.Combine(_previewDirectory, $"preview-{++_previewSequence}.pdf");
            await _editor.ExportRoadbookPdfAsync(previewPath, CreateOptions());
            _activePreviewUri = new Uri(Path.GetFullPath(previewPath));
            _previewWebView.Source = _activePreviewUri;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not refresh the Avalonia roadbook print preview.");
            SetBusy(true, Text("scriptEditor.printDialog.previewFailed"));
        }
        finally
        {
            _printLock.Release();
        }
    }

    private void PreviewWebView_NavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs eventArgs)
    {
        if (_disposed || _activePreviewUri == null || eventArgs.Request != _activePreviewUri)
        {
            return;
        }

        if (eventArgs.IsSuccess)
        {
            SetBusy(false, string.Empty);
            return;
        }

        Logger.Warn("Avalonia roadbook print preview navigation failed.");
        SetBusy(true, Text("scriptEditor.printDialog.previewFailed"));
    }

    private async void ExportPdf_Click(object? sender, FAContentDialogButtonClickEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        if (_isBusy)
        {
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null)
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "pdf",
            SuggestedFileName = "roadbook.pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }]
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await _printLock.WaitAsync();
        try
        {
            SetBusy(true, Text("scriptEditor.printDialog.exporting"));
            await _editor.ExportRoadbookPdfAsync(path, CreateOptions());
            _dialog?.Hide(FAContentDialogResult.Primary);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not export roadbook PDF to {0}.", path);
            await ShowErrorAsync(exception.Message);
        }
        finally
        {
            SetBusy(false, string.Empty);
            _printLock.Release();
        }
    }

    private void Print_Click(object? sender, FAContentDialogButtonClickEventArgs eventArgs)
    {
        if (_isBusy)
        {
            eventArgs.Cancel = true;
            return;
        }

        _pendingSystemPrint = CreateOptions();
        Logger.Info("System roadbook print requested from the settings dialog.");
        // Do not cancel this event. FluentAvalonia's normal Secondary-button close path
        // completes after this handler; calling Hide while that deferral is active is ignored.
    }

    private RoadbookPrintOptions CreateOptions()
    {
        var paper = _paperBox.SelectedIndex switch
        {
            0 => (297d, 420d),
            2 => (148d, 210d),
            3 => (215.9d, 279.4d),
            4 => (215.9d, 355.6d),
            _ => (210d, 297d)
        };
        return new RoadbookPrintOptions(
            paper.Item1,
            paper.Item2,
            _orientationBox.SelectedIndex == 1,
            (double)(_horizontalMarginBox.Value ?? 15m),
            (double)(_verticalMarginBox.Value ?? 10m),
            ShowPageNumbers: _pageNumbersToggle.IsChecked == true,
            PageNumberStyle: _pageNumberStyleBox.SelectedIndex == 1
                ? RoadbookPageNumberStyle.Current
                : RoadbookPageNumberStyle.CurrentAndTotal,
            PageNumberPosition: _pageNumberPositionBox.SelectedIndex switch
            {
                0 => RoadbookPageNumberPosition.BottomLeft,
                2 => RoadbookPageNumberPosition.BottomRight,
                _ => RoadbookPageNumberPosition.BottomCenter
            });
    }

    private void SetBusy(bool busy, string status)
    {
        _isBusy = busy;
        _previewStatusText.Text = status;
        _previewLoading.IsVisible = busy;
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = Text("scriptEditor.errorTitle"),
            Content = message,
            CloseButtonText = AvaloniaLocalization.Get("dialog.common.btn_ok")
        });
        await dialog.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private static string Text(string key) => AvaloniaLocalization.Get(key);

    private T Find<T>(string name) where T : Control => this.FindControl<T>(name) ??
        throw new InvalidOperationException($"The print dialog control '{name}' was not created.");
}
