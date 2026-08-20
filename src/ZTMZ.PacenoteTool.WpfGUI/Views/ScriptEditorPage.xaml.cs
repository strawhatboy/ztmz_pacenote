using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.ScriptEditor;

namespace ZTMZ.PacenoteTool.WpfGUI.Views;

public partial class ScriptEditorPage : Page
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private const string PacenoteFilter = "Pacenote scripts (*.pacenote)|*.pacenote|All files (*.*)|*.*";
    private readonly ZTMZPacenoteTool _tool;
    private readonly IContentDialogService _contentDialogService;
    private PacenoteEditorControl Editor { get; }
    private string _currentFile = string.Empty;
    private string _playbackScriptPath = string.Empty;
    private string _documentName = string.Empty;
    private bool _isDirty;
    private bool _isReadOnly;
    private bool _initialized;
    private bool _loadingRoadbookScale;

    public ScriptEditorPage(ZTMZPacenoteTool tool, IContentDialogService contentDialogService)
    {
        _tool = tool;
        _contentDialogService = contentDialogService;
        InitializeComponent();
        Editor = new PacenoteEditorControl();
        EditorHost.Content = Editor;
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
            await Editor.RefreshPacenoteLanguageAsync();
            return;
        }

        try
        {
            await Editor.InitializeAsync();
            _initialized = true;
            _loadingRoadbookScale = true;
            try
            {
                RoadbookScale.Value = await Editor.GetRoadbookScaleAsync();
                UpdateRoadbookScaleText();
            }
            finally
            {
                _loadingRoadbookScale = false;
            }
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

    private async void PrintRoadbook_Click(object sender, RoutedEventArgs e)
    {
        var editorVisibility = Editor.Visibility;
        Editor.Visibility = Visibility.Collapsed;
        try
        {
            var dialog = new RoadbookPrintWindow(
                _contentDialogService.GetDialogHostEx()!,
                Editor);
            await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Roadbook print dialog failed.");
            await ShowErrorAsync(exception.Message);
        }
        finally
        {
            Editor.Visibility = editorVisibility;
        }
    }

    private async void WordWrap_Changed(object sender, RoutedEventArgs e)
    {
        if (Editor?.IsReady == true && sender is ToggleSwitch toggle)
        {
            await Editor.SetWordWrapAsync(toggle.IsChecked == true);
        }
    }

    private async void RoadbookScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateRoadbookScaleText();
        if (!_loadingRoadbookScale && Editor?.IsReady == true)
        {
            await Editor.SetRoadbookScaleAsync(e.NewValue);
        }
    }

    private void UpdateRoadbookScaleText()
    {
        if (RoadbookScaleValue != null && RoadbookScale != null)
        {
            RoadbookScaleValue.Text = $"{RoadbookScale.Value:0}%";
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
