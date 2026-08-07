using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class ScriptEditorPage : Page
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private const string PacenoteFilter = "Pacenote scripts (*.pacenote)|*.pacenote|All files (*.*)|*.*";
    private const string RoadbookPdfFilter = "PDF files (*.pdf)|*.pdf";
    private readonly ZTMZPacenoteTool _tool;
    private string _currentFile = string.Empty;
    private string _playbackScriptPath = string.Empty;
    private string _documentName = string.Empty;
    private bool _isDirty;
    private bool _isReadOnly;
    private bool _initialized;

    public ScriptEditorPage(ZTMZPacenoteTool tool)
    {
        _tool = tool;
        InitializeComponent();
        Editor.ContentChanged += (_, _) =>
        {
            _isDirty = true;
            UpdateStatus();
        };
        _tool.onPacenoteDispatched += OnPacenoteDispatched;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            await Editor.InitializeAsync();
            _initialized = true;
            await UpdateRoadbookTitleAsync();
            UpdateStatus();
            await Editor.FocusEditorAsync();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Script editor initialization failed.");
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = PacenoteFilter, Multiselect = false };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await OpenScriptAsync(dialog.FileName);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync(false);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync(true);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = "CrewChief JSON (*.json)|*.json", Multiselect = false };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var script = ScriptReader.ReadFromJson(dialog.FileName).ToString();
            await Editor.SetReadOnlyAsync(false);
            await Editor.SetTextAsync(script);
            _currentFile = string.Empty;
            _playbackScriptPath = string.Empty;
            _documentName = string.Empty;
            _isReadOnly = false;
            _isDirty = true;
            await UpdateRoadbookTitleAsync();
            UpdateStatus();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        await Editor.ValidateAsync();
    }

    private async void ExportRoadbookPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = RoadbookPdfFilter,
            DefaultExt = ".pdf",
            FileName = "roadbook.pdf"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            if (!await Editor.ExportRoadbookPdfAsync(dialog.FileName))
            {
                await ShowErrorAsync(I18NLoader.Instance["scriptEditor.exportRoadbookPdfFailed"]);
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Roadbook PDF export failed.");
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void WordWrap_Changed(object sender, RoutedEventArgs e)
    {
        if (Editor?.IsReady == true && sender is ToggleSwitch toggle)
        {
            await Editor.SetWordWrapAsync(toggle.IsChecked == true);
        }
    }

    private async void AdjustDistance_Click(object sender, RoutedEventArgs e)
    {
        var adjustment = DistanceAdjustment.Value ?? 0;
        if (Math.Abs(adjustment) < double.Epsilon)
        {
            return;
        }

        var content = await Editor.GetTextAsync();
        var updated = Regex.Replace(
            content,
            @"(?m)^(\s*)(-?\d+(?:\.\d+)?)(?=\s*[:,])",
            match =>
            {
                if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
                {
                    return match.Value;
                }

                return match.Groups[1].Value + (distance + adjustment).ToString("0.###", CultureInfo.InvariantCulture);
            });
        await Editor.SetTextAsync(updated);
        _isDirty = true;
        UpdateStatus();
    }

    public async Task<bool> OpenScriptAsync(string fileName)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return false;
        }

        try
        {
            await Editor.SetReadOnlyAsync(false);
            await Editor.SetTextAsync(await File.ReadAllTextAsync(fileName));
            _currentFile = Path.GetFullPath(fileName);
            _playbackScriptPath = _currentFile;
            _documentName = string.Empty;
            _isReadOnly = false;
            _isDirty = false;
            await UpdateRoadbookTitleAsync();
            UpdateStatus();
            return true;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
            return false;
        }
    }

    public async Task<bool> OpenReadOnlyScriptAsync(string contents, string displayName, string playbackScriptPath)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return false;
        }

        await Editor.SetTextAsync(contents);
        await Editor.SetReadOnlyAsync(true);
        _currentFile = string.Empty;
        _playbackScriptPath = playbackScriptPath ?? string.Empty;
        _documentName = displayName ?? string.Empty;
        _isReadOnly = true;
        _isDirty = false;
        await UpdateRoadbookTitleAsync();
        UpdateStatus();
        return true;
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_isReadOnly)
        {
            return false;
        }

        if (saveAs || string.IsNullOrWhiteSpace(_currentFile))
        {
            var dialog = new SaveFileDialog { Filter = PacenoteFilter, DefaultExt = ".pacenote" };
            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            _currentFile = dialog.FileName;
        }

        try
        {
            await File.WriteAllTextAsync(_currentFile, await Editor.GetTextAsync());
            _isDirty = false;
            await UpdateRoadbookTitleAsync();
            UpdateStatus();
            return true;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
            return false;
        }
    }

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!_isDirty)
        {
            return true;
        }

        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = I18NLoader.Instance["scriptEditor.unsavedTitle"],
            Content = I18NLoader.Instance["scriptEditor.unsavedContent"],
            PrimaryButtonText = I18NLoader.Instance["scriptEditor.save"],
            SecondaryButtonText = I18NLoader.Instance["scriptEditor.discard"],
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_cancel"]
        }.ShowDialogAsync();

        return result switch
        {
            Wpf.Ui.Controls.MessageBoxResult.Primary => await SaveAsync(false),
            Wpf.Ui.Controls.MessageBoxResult.Secondary => true,
            _ => false
        };
    }

    private void OnPacenoteDispatched(PacenotePlaybackSnapshot snapshot)
    {
        if (snapshot.Source != "script" || string.IsNullOrWhiteSpace(_playbackScriptPath) ||
            !string.Equals(snapshot.ScriptPath, _playbackScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.InvokeAsync(async () => await Editor.HighlightPlaybackAsync(snapshot));
    }

    private void UpdateStatus()
    {
        StatusText.Text = _isReadOnly
            ? string.Format(I18NLoader.Instance["scriptEditor.readOnly"], _documentName)
            : _isDirty
            ? I18NLoader.Instance["scriptEditor.unsaved"]
            : string.IsNullOrEmpty(_currentFile)
                ? I18NLoader.Instance["scriptEditor.newDocument"]
                : Path.GetFileName(_currentFile);
    }

    private Task UpdateRoadbookTitleAsync()
    {
        var title = !string.IsNullOrEmpty(_currentFile)
            ? Path.GetFileNameWithoutExtension(_currentFile)
            : !string.IsNullOrEmpty(_documentName)
                ? _documentName
                : I18NLoader.Instance["scriptEditor.newDocument"];
        return Editor.SetRoadbookTitleAsync(title);
    }

    private Task ShowErrorAsync(string message)
    {
        return new Wpf.Ui.Controls.MessageBox
        {
            Title = I18NLoader.Instance["scriptEditor.errorTitle"],
            Content = message,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
        }.ShowDialogAsync();
    }
}
