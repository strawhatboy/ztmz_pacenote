#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Avalonia.Models;

namespace ZTMZ.PacenoteTool.Avalonia.Views.GameSettings;

public sealed class GameIntegrationSettingsPane : GameSettingsPane
{
    private readonly IGameConfig _configuration;
    private readonly IGameSettingsProvider _provider;
    private readonly IGameIntegrationProvider? _integrationProvider;
    private readonly IReadOnlyList<GameSettingField> _fields;
    private readonly Dictionary<string, Control> _fieldControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _actionButtons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _executingActionIds = new(StringComparer.Ordinal);
    private readonly TextBlock _statusTextBlock;
    private readonly DispatcherTimer? _statusRefreshTimer;
    private IGame? _game;
    private bool _isRefreshing;

    public GameIntegrationSettingsPane(IGameConfig configuration, IGameSettingsProvider provider)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _integrationProvider = provider as IGameIntegrationProvider;
        _fields = provider.GetFields(configuration);

        var root = new StackPanel { Spacing = 8 };

        foreach (var field in _fields)
        {
            AddField(root, field);
        }

        foreach (var informationKey in _integrationProvider?.InformationKeys ?? [])
        {
            var information = new TextBlock
            {
                Margin = new Thickness(0, 4),
                TextWrapping = TextWrapping.WrapWithOverflow,
                VerticalAlignment = VerticalAlignment.Center
            };
            var localized = I18NLoader.Instance[informationKey];
            information.Text = localized ?? informationKey;
            root.Children.Add(information);
        }

        _statusTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 4),
            TextWrapping = TextWrapping.WrapWithOverflow,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (_integrationProvider is not null)
            root.Children.Add(_statusTextBlock);

        if (_integrationProvider?.Actions.Count > 0)
        {
            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0),
                Spacing = 8
            };
            foreach (var action in _integrationProvider.Actions)
            {
                var button = new Button
                {
                    Margin = new Thickness(0),
                    Tag = action,
                    Content = I18NLoader.Instance[action.LabelKey] ?? action.LabelKey
                };
                if (!string.IsNullOrWhiteSpace(action.ToolTipKey))
                {
                    ToolTip.SetTip(button, I18NLoader.Instance[action.ToolTipKey]);
                    ToolTip.SetShowDelay(button, 100);
                }
                button.Click += ActionButton_OnClick;
                actionPanel.Children.Add(button);
                _actionButtons[action.Id] = button;
            }
            root.Children.Add(actionPanel);
        }

        Content = root;

        if (_integrationProvider?.StatusRefreshInterval > TimeSpan.Zero)
        {
            _statusRefreshTimer = new DispatcherTimer
            {
                Interval = _integrationProvider.StatusRefreshInterval
            };
            _statusRefreshTimer.Tick += (_, _) => RefreshStatus();
        }

        AttachedToVisualTree += (_, _) => _statusRefreshTimer?.Start();
        DetachedFromVisualTree += (_, _) => _statusRefreshTimer?.Stop();

        RefreshFromConfiguration();
    }

    public override void InitializeWithGame(IGame game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        RefreshStatus();
    }

    public override void RefreshFromConfiguration()
    {
        _isRefreshing = true;
        try
        {
            foreach (var field in _fields)
            {
                if (!_fieldControls.TryGetValue(field.Id, out var control))
                    continue;

                var value = _provider.GetFieldValue(_configuration, field.Id);
                switch (control)
                {
                    case CheckBox checkBox:
                        checkBox.IsChecked = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                        break;
                    case NumericUpDown numericUpDown:
                        numericUpDown.Value = value is null
                            ? null
                            : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                        break;
                    case TextBox textBox:
                        textBox.Text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
                        break;
                }
            }
        }
        finally
        {
            _isRefreshing = false;
        }
        RefreshStatus();
    }

    private void AddField(StackPanel root, GameSettingField field)
    {
        if (field.Kind == GameSettingFieldKind.Boolean)
        {
            var checkBox = new CheckBox
            {
                MinHeight = 40,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = field,
                Content = I18NLoader.Instance[field.LabelKey] ?? field.LabelKey
            };
            if (!string.IsNullOrWhiteSpace(field.ToolTipKey))
            {
                ToolTip.SetTip(checkBox, I18NLoader.Instance[field.ToolTipKey]);
                ToolTip.SetShowDelay(checkBox, 100);
            }
            checkBox.IsCheckedChanged += BooleanField_OnChanged;
            root.Children.Add(checkBox);
            _fieldControls[field.Id] = checkBox;
            return;
        }

        var row = new Grid
        {
            MinHeight = 40,
            ColumnDefinitions = new ColumnDefinitions("*,220"),
            ColumnSpacing = 8
        };
        var label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.WrapWithOverflow,
            Text = I18NLoader.Instance[field.LabelKey] ?? field.LabelKey
        };
        if (!string.IsNullOrWhiteSpace(field.ToolTipKey))
        {
            ToolTip.SetTip(label, I18NLoader.Instance[field.ToolTipKey]);
            ToolTip.SetShowDelay(label, 100);
        }
        row.Children.Add(label);

        Control editor;
        if (field.Kind == GameSettingFieldKind.Number)
        {
            var numericUpDown = new NumericUpDown
            {
                VerticalAlignment = VerticalAlignment.Center,
                Tag = field,
                Minimum = field.Minimum is null ? decimal.MinValue : Convert.ToDecimal(field.Minimum.Value),
                Maximum = field.Maximum is null ? decimal.MaxValue : Convert.ToDecimal(field.Maximum.Value)
            };
            numericUpDown.ValueChanged += (_, _) => ApplyNumericFieldValue(numericUpDown, field);
            editor = numericUpDown;
        }
        else
        {
            var textBox = new TextBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Tag = field
            };
            textBox.TextChanged += TextField_OnChanged;

            if (field.Kind is GameSettingFieldKind.DirectoryPath or GameSettingFieldKind.FilePath)
            {
                var pickerGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 4
                };
                pickerGrid.Children.Add(textBox);

                var pickerButton = new Button
                {
                    Tag = (field, textBox),
                    Content = new TextBlock { Text = "…" },
                    Width = 32,
                    Height = 32
                };
                if (!string.IsNullOrWhiteSpace(field.ToolTipKey))
                {
                    ToolTip.SetTip(pickerButton, I18NLoader.Instance[field.ToolTipKey]);
                    ToolTip.SetShowDelay(pickerButton, 100);
                }
                pickerButton.Click += PickerButton_OnClick;
                Grid.SetColumn(pickerButton, 1);
                pickerGrid.Children.Add(pickerButton);
                editor = pickerGrid;
                _fieldControls[field.Id] = textBox;
            }
            else
            {
                editor = textBox;
            }
        }

        if (!_fieldControls.ContainsKey(field.Id))
            _fieldControls[field.Id] = editor;
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        root.Children.Add(row);
    }

    private async void BooleanField_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing || sender is not CheckBox checkBox || checkBox.Tag is not GameSettingField field)
            return;

        var requestedValue = checkBox.IsChecked == true;
        var confirmation = field.Confirmation;
        if (confirmation is not null && (!confirmation.WhenEnablingOnly || requestedValue))
        {
            var confirmed = await ConfirmAsync(confirmation);
            if (!confirmed)
            {
                _isRefreshing = true;
                checkBox.IsChecked = !requestedValue;
                _isRefreshing = false;
                return;
            }
        }

        ApplyFieldValue(field, requestedValue);
    }

    private void ApplyNumericFieldValue(NumericUpDown numericUpDown, GameSettingField field)
    {
        if (_isRefreshing || numericUpDown.Value is null)
            return;
        ApplyFieldValue(field, (double)numericUpDown.Value.Value);
    }

    private void TextField_OnChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isRefreshing || sender is not TextBox textBox || textBox.Tag is not GameSettingField field)
            return;
        ApplyFieldValue(field, textBox.Text);
    }

    private async void PickerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: (GameSettingField field, TextBox textBox) })
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        if (field.Kind == GameSettingFieldKind.DirectoryPath)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { AllowMultiple = false });
            if (folders.Count > 0)
                textBox.Text = folders[0].Path.LocalPath;
            return;
        }

        var options = new FilePickerOpenOptions { AllowMultiple = false };
        if (!string.IsNullOrWhiteSpace(field.FileExtension))
        {
            var extension = field.FileExtension.TrimStart('.');
            options.FileTypeFilter =
            [
                new FilePickerFileType(extension.ToUpperInvariant())
                {
                    Patterns = [$"*.{extension}"]
                }
            ];
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
            textBox.Text = files[0].Path.LocalPath;
    }

    private void ApplyFieldValue(GameSettingField field, object? value)
    {
        if (_game is null)
            return;

        _provider.SetFieldValue(_configuration, field.Id, value, _game);
        Config.Instance.SaveGameConfig(_game);
        if (field.RequiresRestart)
            RaiseRestartNeeded();
        RefreshStatus();
    }

    private async void ActionButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_game is null || _integrationProvider is null || sender is not Button button || button.Tag is not GameIntegrationAction action)
            return;

        if (action.Confirmation is not null && !await ConfirmAsync(action.Confirmation))
            return;

        button.IsEnabled = false;
        _executingActionIds.Add(action.Id);
        GameIntegrationActionResult result;
        try
        {
            result = await _integrationProvider.ExecuteActionAsync(
                action.Id, _configuration, _game, CancellationToken.None);
        }
        catch (Exception exception)
        {
            result = new GameIntegrationActionResult(false, exception.Message);
        }
        finally
        {
            _executingActionIds.Remove(action.Id);
            RefreshStatus();
        }

        if (!result.Succeeded || action.ShowSuccessMessage)
        {
            var titleKey = result.Succeeded ? action.SuccessTitleKey : action.FailureTitleKey;
            await ShowMessageDialog(
                I18NLoader.Instance[titleKey ?? action.LabelKey] ?? titleKey ?? action.LabelKey,
                result.MessageIsResourceKey
                    ? Localize(result.Message, result.MessageArguments)
                    : result.Message);
        }

        RefreshStatus();
    }

    private async Task<bool> ConfirmAsync(GameSettingConfirmation confirmation)
    {
        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = I18NLoader.Instance[confirmation.TitleKey] ?? confirmation.TitleKey,
            Content = I18NLoader.Instance[confirmation.MessageKey] ?? confirmation.MessageKey,
            PrimaryButtonText = I18NLoader.Instance[confirmation.PrimaryButtonKey] ?? confirmation.PrimaryButtonKey,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_cancel"] ?? "Cancel",
            DefaultButton = FAContentDialogButton.Primary
        });

        var result = await dialog.ShowAsync();
        return result == FAContentDialogResult.Primary;
    }

    private async Task ShowMessageDialog(string title, string message)
    {
        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"] ?? "OK"
        });
        await dialog.ShowAsync();
    }

    private void RefreshStatus()
    {
        if (_game is null || _integrationProvider is null)
            return;

        try
        {
            var status = _integrationProvider.GetStatus(_configuration, _game);
            var localizedMessage = I18NLoader.Instance[status.MessageKey] ?? status.MessageKey;
            _statusTextBlock.Text = status.MessageArguments is { Count: > 0 }
                ? string.Format(CultureInfo.CurrentCulture, localizedMessage, status.MessageArguments.ToArray())
                : localizedMessage;
            foreach (var (actionId, button) in _actionButtons)
            {
                button.IsEnabled = !_executingActionIds.Contains(actionId) &&
                    (status.EnabledActionIds is null || status.EnabledActionIds.Contains(actionId));
            }
        }
        catch (Exception exception)
        {
            _statusTextBlock.Text = exception.Message;
            foreach (var button in _actionButtons.Values)
                button.IsEnabled = false;
        }
    }

    private static string Localize(string key, IReadOnlyList<object>? arguments = null)
    {
        var text = I18NLoader.Instance[key] ?? key;
        return arguments is { Count: > 0 }
            ? string.Format(CultureInfo.CurrentCulture, text, arguments.ToArray())
            : text;
    }

    public override void Dispose()
    {
        _statusRefreshTimer?.Stop();
        base.Dispose();
    }
}
