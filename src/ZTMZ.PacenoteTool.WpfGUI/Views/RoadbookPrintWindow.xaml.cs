using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.ScriptEditor;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class RoadbookPrintWindow : ContentDialog
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly PacenoteEditorControl _editor;
    private readonly string _previewDirectory = Path.Combine(
        Path.GetTempPath(),
        "ZTMZ-roadbook-print-preview",
        Guid.NewGuid().ToString("N"));
    private readonly List<string> _previewFiles = [];
    private readonly SemaphoreSlim _printLock = new(1, 1);
    private CancellationTokenSource? _previewCancellation;
    private int _previewSequence;
    private ulong _activePreviewNavigationId;
    private bool _isLoaded;
    private bool _isClosed;

    public RoadbookPrintWindow(ContentDialogHost dialogHost, PacenoteEditorControl editor)
        : base(dialogHost)
    {
        _editor = editor;
        InitializeComponent();
        InitializeText();
        InitializeChoices();
    }

    private async void OnOpened(object sender, RoutedEventArgs e)
    {
        Opened -= OnOpened;
        Directory.CreateDirectory(_previewDirectory);
        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();
            PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            PreviewWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _isLoaded = true;
            SchedulePreview(TimeSpan.Zero);
        }
        catch (Exception exception)
        {
            if (!_isClosed)
            {
                ShowPreviewError(exception.Message);
            }
        }
    }

    private void InitializeText()
    {
        Title = Text("scriptEditor.printDialog.title", "Print roadbook");
        DestinationLabel.Text = Text("scriptEditor.printDialog.destination", "Destination");
        LayoutHeading.Text = Text("scriptEditor.printDialog.layout", "Layout");
        PaperLabel.Text = Text("scriptEditor.printDialog.paper", "Paper size");
        OrientationLabel.Text = Text("scriptEditor.printDialog.orientation", "Orientation");
        MarginsLabel.Text = Text("scriptEditor.printDialog.margins", "Margins (mm)");
        HorizontalMarginLabel.Text = Text("scriptEditor.printDialog.horizontal", "Left / right");
        VerticalMarginLabel.Text = Text("scriptEditor.printDialog.vertical", "Top / bottom");
        PageNumbersLabel.Text = Text("scriptEditor.printDialog.pageNumbers", "Page numbers");
        PageNumbersToggle.Content = Text("scriptEditor.printDialog.showPageNumbers", "Show");
        PageRangesLabel.Text = Text("scriptEditor.printDialog.pages", "Pages");
        PageRangesHint.Text = Text("scriptEditor.printDialog.pagesHint", "Leave blank for all pages, or enter 1-3, 5.");
        CopiesLabel.Text = Text("scriptEditor.printDialog.copies", "Copies");
        PreviewStatusText.Text = Text("scriptEditor.printDialog.preparingPreview", "Preparing preview...");
        CancelButton.Content = Text("scriptEditor.printDialog.cancel", "Cancel");
    }

    private void InitializeChoices()
    {
        var destinations = new List<PrintDestination>
        {
            new(Text("scriptEditor.printDialog.savePdf", "Save as PDF"), string.Empty)
        };
        try
        {
            destinations.AddRange(
                System.Drawing.Printing.PrinterSettings.InstalledPrinters
                    .Cast<string>()
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(name => new PrintDestination(name, name)));
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Could not enumerate printers for the roadbook print dialog.");
        }

        DestinationBox.ItemsSource = destinations;
        DestinationBox.SelectedIndex = 0;
        PaperBox.ItemsSource = new[]
        {
            new PaperChoice("A3", 297, 420),
            new PaperChoice("A4", 210, 297),
            new PaperChoice("A5", 148, 210),
            new PaperChoice("Letter", 215.9, 279.4),
            new PaperChoice("Legal", 215.9, 355.6)
        };
        PaperBox.SelectedIndex = 1;
        OrientationBox.ItemsSource = new[]
        {
            new OrientationChoice(Text("scriptEditor.printDialog.portrait", "Portrait"), false),
            new OrientationChoice(Text("scriptEditor.printDialog.landscape", "Landscape"), true)
        };
        OrientationBox.SelectedIndex = 0;
        PageNumberStyleBox.ItemsSource = new[]
        {
            new PageNumberStyleChoice("1 / N", RoadbookPageNumberStyle.CurrentAndTotal),
            new PageNumberStyleChoice("1", RoadbookPageNumberStyle.Current)
        };
        PageNumberStyleBox.SelectedIndex = 0;
        PageNumberPositionBox.ItemsSource = new[]
        {
            new PageNumberPositionChoice(
                Text("scriptEditor.printDialog.pageNumberBottomLeft", "Bottom left"),
                RoadbookPageNumberPosition.BottomLeft),
            new PageNumberPositionChoice(
                Text("scriptEditor.printDialog.pageNumberBottomCenter", "Bottom center"),
                RoadbookPageNumberPosition.BottomCenter),
            new PageNumberPositionChoice(
                Text("scriptEditor.printDialog.pageNumberBottomRight", "Bottom right"),
                RoadbookPageNumberPosition.BottomRight)
        };
        PageNumberPositionBox.SelectedIndex = 1;
        UpdateDestinationState();
    }

    private void Destination_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateDestinationState();
    }

    private void UpdateDestinationState()
    {
        var isPrinter = (DestinationBox.SelectedItem as PrintDestination)?.IsPrinter == true;
        CopiesPanel.Visibility = isPrinter ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Content = isPrinter
            ? Text("scriptEditor.printDialog.print", "Print")
            : Text("scriptEditor.printDialog.export", "Export PDF");
    }

    private void PreviewSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        SchedulePreview();
    }

    private void PreviewNumberSetting_Changed(object sender, NumberBoxValueChangedEventArgs e)
    {
        SchedulePreview();
    }

    private void PageRanges_Changed(object sender, TextChangedEventArgs e)
    {
        SchedulePreview();
    }

    private void PageNumberSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (PageNumberStyleBox == null || PageNumberPositionBox == null)
        {
            return;
        }

        var isEnabled = PageNumbersToggle.IsChecked == true;
        PageNumberStyleBox.IsEnabled = isEnabled;
        PageNumberPositionBox.IsEnabled = isEnabled;
        SchedulePreview();
    }

    private void SchedulePreview(TimeSpan? delay = null)
    {
        if (!_isLoaded || _isClosed)
        {
            return;
        }

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        _ = RefreshPreviewAsync(delay ?? TimeSpan.FromMilliseconds(350), _previewCancellation.Token);
    }

    private async Task RefreshPreviewAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await _printLock.WaitAsync(cancellationToken);
            try
            {
                if (_isClosed)
                {
                    return;
                }

                SetBusy(true, Text("scriptEditor.printDialog.preparingPreview", "Preparing preview..."));
                var previewPath = Path.Combine(_previewDirectory, $"preview-{++_previewSequence}.pdf");
                if (!await _editor.ExportRoadbookPdfAsync(previewPath, CreateOptions()))
                {
                    throw new InvalidOperationException(Text("scriptEditor.printDialog.previewFailed", "The print preview could not be created."));
                }

                _previewFiles.Add(previewPath);
                if (_isClosed)
                {
                    return;
                }

                PreviewWebView.Source = CreatePreviewUri(previewPath);
                Logger.Debug("Roadbook print preview generated at {0} ({1} bytes).", previewPath, new FileInfo(previewPath).Length);
            }
            finally
            {
                _printLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_isClosed)
            {
                ShowPreviewError(exception.Message);
            }
        }
    }

    private void PreviewWebView_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        _activePreviewNavigationId = e.NavigationId;
    }

    private void PreviewWebView_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_isClosed || e.NavigationId != _activePreviewNavigationId)
        {
            return;
        }

        if (e.IsSuccess)
        {
            Logger.Debug(
                "Roadbook print preview navigation completed. Layout={0:F0}x{1:F0}, WebView={2:F0}x{3:F0}.",
                PrintLayoutRoot.ActualWidth,
                PrintLayoutRoot.ActualHeight,
                PreviewWebView.ActualWidth,
                PreviewWebView.ActualHeight);
            SetBusy(false, string.Empty);
            return;
        }

        Logger.Warn(
            "Roadbook print preview navigation failed with status {0}.",
            e.WebErrorStatus);
        ShowPreviewError(Text(
            "scriptEditor.printDialog.previewFailed",
            "The print preview could not be created."));
    }

    private static Uri CreatePreviewUri(string previewPath)
    {
        var fileUri = new Uri(Path.GetFullPath(previewPath));
        return new Uri(fileUri.AbsoluteUri + "#toolbar=0&navpanes=0", UriKind.Absolute);
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        var destination = DestinationBox.SelectedItem as PrintDestination;
        if (destination == null)
        {
            return;
        }

        string? outputPath = null;
        if (!destination.IsPrinter)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PDF files (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = "roadbook.pdf"
            };
            if (dialog.ShowDialog(System.Windows.Window.GetWindow(this)) != true)
            {
                return;
            }

            outputPath = dialog.FileName;
        }

        _previewCancellation?.Cancel();
        await _printLock.WaitAsync();
        try
        {
            SetBusy(true, destination.IsPrinter
                ? Text("scriptEditor.printDialog.printing", "Sending to printer...")
                : Text("scriptEditor.printDialog.exporting", "Exporting PDF..."));
            var options = CreateOptions(destination.PrinterName);
            if (destination.IsPrinter)
            {
                var result = await _editor.PrintRoadbookAsync(options);
                if (result != RoadbookPrintResult.Succeeded)
                {
                    throw new InvalidOperationException(result == RoadbookPrintResult.PrinterUnavailable
                        ? Text("scriptEditor.printDialog.printerUnavailable", "The selected printer is unavailable.")
                        : Text("scriptEditor.printDialog.printFailed", "The roadbook could not be printed."));
                }
            }
            else if (!await _editor.ExportRoadbookPdfAsync(outputPath!, options))
            {
                throw new InvalidOperationException(Text("scriptEditor.printDialog.exportFailed", "The roadbook PDF could not be created."));
            }

            Hide(ContentDialogResult.Primary);
        }
        catch (Exception exception)
        {
            SetBusy(false, string.Empty);
            var owner = System.Windows.Window.GetWindow(this);
            if (owner == null)
            {
                System.Windows.MessageBox.Show(
                    exception.Message,
                    Title?.ToString() ?? string.Empty,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    owner,
                    exception.Message,
                    Title?.ToString() ?? string.Empty,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        finally
        {
            _printLock.Release();
        }
    }

    private RoadbookPrintOptions CreateOptions(string printerName = "")
    {
        var paper = PaperBox.SelectedItem as PaperChoice ?? new PaperChoice("A4", 210, 297);
        var orientation = OrientationBox.SelectedItem as OrientationChoice;
        var pageNumberStyle = PageNumberStyleBox.SelectedItem as PageNumberStyleChoice;
        var pageNumberPosition = PageNumberPositionBox.SelectedItem as PageNumberPositionChoice;
        return new RoadbookPrintOptions(
            paper.WidthMillimeters,
            paper.HeightMillimeters,
            orientation?.Landscape == true,
            HorizontalMarginBox.Value ?? 15,
            VerticalMarginBox.Value ?? 10,
            PageRangesBox.Text,
            (int)Math.Round(CopiesBox.Value ?? 1),
            printerName,
            PageNumbersToggle.IsChecked == true,
            pageNumberStyle?.Style ?? RoadbookPageNumberStyle.CurrentAndTotal,
            pageNumberPosition?.Position ?? RoadbookPageNumberPosition.BottomCenter);
    }

    private void SetBusy(bool isBusy, string message)
    {
        PreviewStatusText.Text = message;
        PreviewLoading.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.IsEnabled = !isBusy;
    }

    private void ShowPreviewError(string message)
    {
        PreviewStatusText.Text = message;
        PreviewLoading.Visibility = Visibility.Visible;
        PrimaryButton.IsEnabled = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Hide(ContentDialogResult.None);
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        await _printLock.WaitAsync();
        try
        {
            PreviewWebView.Dispose();
            foreach (var file in _previewFiles)
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
                Directory.Delete(_previewDirectory, false);
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
        }
    }

    private static string Text(string key, string fallback)
    {
        try
        {
            var value = I18NLoader.Instance[key];
            return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private sealed record PrintDestination(string DisplayName, string PrinterName)
    {
        public bool IsPrinter => !string.IsNullOrWhiteSpace(PrinterName);
    }

    private sealed record PaperChoice(string DisplayName, double WidthMillimeters, double HeightMillimeters);
    private sealed record OrientationChoice(string DisplayName, bool Landscape);
    private sealed record PageNumberStyleChoice(string DisplayName, RoadbookPageNumberStyle Style);
    private sealed record PageNumberPositionChoice(string DisplayName, RoadbookPageNumberPosition Position);
}
