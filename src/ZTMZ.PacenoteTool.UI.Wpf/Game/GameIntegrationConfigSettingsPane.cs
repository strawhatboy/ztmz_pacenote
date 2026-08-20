#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using TextBox = System.Windows.Controls.TextBox;

namespace ZTMZ.PacenoteTool.UI.Windows.Game;

public sealed class GameIntegrationConfigSettingsPane : IGameConfigSettingsPane
{
    private readonly IGameConfig _configuration;
    private readonly IGameSettingsProvider _provider;
    private readonly IGameIntegrationProvider? _integrationProvider;
    private readonly IReadOnlyList<GameSettingField> _fields;
    private readonly Dictionary<string, FrameworkElement> _fieldControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Wpf.Ui.Controls.Button> _actionButtons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _executingActionIds = new(StringComparer.Ordinal);
    private readonly Wpf.Ui.Controls.TextBlock _statusTextBlock;
    private readonly DispatcherTimer? _statusRefreshTimer;
    private IGame? _game;
    private bool _isRefreshing;

    public GameIntegrationConfigSettingsPane(
        IGameConfig configuration,
        IGameSettingsProvider provider)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _integrationProvider = provider as IGameIntegrationProvider;
        _fields = provider.GetFields(configuration);

        var root = new StackPanel();
        foreach (var field in _fields)
        {
            AddField(root, field);
        }

        foreach (var informationKey in _integrationProvider?.InformationKeys ?? [])
        {
            var information = new Wpf.Ui.Controls.TextBlock
            {
                Margin = new Thickness(0, 4, 0, 4),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            information.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, informationKey);
            root.Children.Add(information);
        }

        _statusTextBlock = new Wpf.Ui.Controls.TextBlock
        {
            Margin = new Thickness(0, 4, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (_integrationProvider is not null)
        {
            root.Children.Add(_statusTextBlock);
        }

        var actionPanel = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        foreach (var action in _integrationProvider?.Actions ?? [])
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Margin = new Thickness(8, 0, 0, 0),
                Tag = action
            };
            button.SetResourceReference(ContentControl.ContentProperty, action.LabelKey);
            if (!string.IsNullOrWhiteSpace(action.ToolTipKey))
            {
                button.SetResourceReference(ToolTipService.ToolTipProperty, action.ToolTipKey);
            }

            button.Click += ActionButton_OnClick;
            actionPanel.Children.Add(button);
            _actionButtons[action.Id] = button;
        }
        if (actionPanel.Children.Count > 0)
        {
            root.Children.Add(actionPanel);
        }

        Content = root;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        if (_integrationProvider?.StatusRefreshInterval > TimeSpan.Zero)
        {
            _statusRefreshTimer = new DispatcherTimer
            {
                Interval = _integrationProvider.StatusRefreshInterval
            };
            _statusRefreshTimer.Tick += (_, _) => RefreshStatus();
        }

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
                {
                    continue;
                }

                var value = _provider.GetFieldValue(_configuration, field.Id);
                switch (control)
                {
                    case CheckBox checkBox:
                        checkBox.IsChecked = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                        break;
                    case NumberBox numberBox:
                        numberBox.Value = value is null
                            ? null
                            : Convert.ToDouble(value, CultureInfo.InvariantCulture);
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

    private void AddField(Panel root, GameSettingField field)
    {
        if (field.Kind == GameSettingFieldKind.Boolean)
        {
            var checkBox = new CheckBox
            {
                MinHeight = 40,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = field
            };
            checkBox.SetResourceReference(ContentControl.ContentProperty, field.LabelKey);
            if (!string.IsNullOrWhiteSpace(field.ToolTipKey))
            {
                checkBox.SetResourceReference(ToolTipService.ToolTipProperty, field.ToolTipKey);
            }

            checkBox.Checked += BooleanField_OnChanged;
            checkBox.Unchecked += BooleanField_OnChanged;
            root.Children.Add(checkBox);
            _fieldControls[field.Id] = checkBox;
            return;
        }

        var row = new Grid { MinHeight = 40 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        var label = new Wpf.Ui.Controls.TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        label.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, field.LabelKey);
        if (!string.IsNullOrWhiteSpace(field.ToolTipKey))
        {
            label.SetResourceReference(ToolTipService.ToolTipProperty, field.ToolTipKey);
        }
        row.Children.Add(label);

        FrameworkElement editor;
        if (field.Kind == GameSettingFieldKind.Number)
        {
            var numberBox = new NumberBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Tag = field,
                Minimum = field.Minimum ?? double.MinValue,
                Maximum = field.Maximum ?? double.MaxValue
            };
            numberBox.ValueChanged += (_, _) => ApplyNumberFieldValue(numberBox, field);
            editor = numberBox;
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
                var pickerGrid = new Grid();
                pickerGrid.ColumnDefinitions.Add(new ColumnDefinition());
                pickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                pickerGrid.Children.Add(textBox);

                var pickerButton = new Wpf.Ui.Controls.Button
                {
                    Icon = new SymbolIcon { Symbol = SymbolRegular.FolderOpen20 },
                    Tag = (field, textBox)
                };
                if (!string.IsNullOrWhiteSpace(field.ToolTipKey))
                {
                    pickerButton.SetResourceReference(ToolTipService.ToolTipProperty, field.ToolTipKey);
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
        {
            _fieldControls[field.Id] = editor;
        }
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        root.Children.Add(row);
    }

    private async void BooleanField_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || sender is not CheckBox checkBox || checkBox.Tag is not GameSettingField field)
        {
            return;
        }

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

    private void ApplyNumberFieldValue(NumberBox numberBox, GameSettingField field)
    {
        if (_isRefreshing || numberBox.Value is null)
        {
            return;
        }

        ApplyFieldValue(field, numberBox.Value.Value);
    }

    private void TextField_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRefreshing || sender is not TextBox textBox || textBox.Tag is not GameSettingField field)
        {
            return;
        }

        ApplyFieldValue(field, textBox.Text.Trim());
    }

    private void PickerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button { Tag: ValueTuple<GameSettingField, TextBox> tag })
        {
            return;
        }

        var (field, textBox) = tag;
        if (field.Kind == GameSettingFieldKind.DirectoryPath)
        {
            var dialog = new OpenFolderDialog
            {
                InitialDirectory = System.IO.Directory.Exists(textBox.Text)
                    ? textBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Multiselect = false
            };
            if (dialog.ShowDialog() == true && dialog.FolderNames.Length > 0)
            {
                textBox.Text = dialog.FolderNames[0];
            }
            return;
        }

        var fileDialog = new OpenFileDialog
        {
            FileName = textBox.Text,
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(field.FileExtension))
        {
            var extension = field.FileExtension.TrimStart('.');
            fileDialog.DefaultExt = extension;
            fileDialog.Filter = $"{extension.ToUpperInvariant()}|*.{extension}";
        }
        if (fileDialog.ShowDialog() == true)
        {
            textBox.Text = fileDialog.FileName;
        }
    }

    private void ApplyFieldValue(GameSettingField field, object? value)
    {
        if (_game is null)
        {
            return;
        }

        _provider.SetFieldValue(_configuration, field.Id, value, _game);
        Config.Instance.SaveGameConfig(_game);
        if (field.RequiresRestart)
        {
            RestartNeeded?.Invoke();
        }
        RefreshStatus();
    }

    private async void ActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_game is null || _integrationProvider is null || sender is not Wpf.Ui.Controls.Button button || button.Tag is not GameIntegrationAction action)
        {
            return;
        }

        if (action.Confirmation is not null && !await ConfirmAsync(action.Confirmation))
        {
            return;
        }

        button.IsEnabled = false;
        _executingActionIds.Add(action.Id);
        GameIntegrationActionResult result;
        try
        {
            result = await _integrationProvider.ExecuteActionAsync(
                action.Id,
                _configuration,
                _game,
                CancellationToken.None);
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
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = Localize(titleKey ?? action.LabelKey),
                Content = result.MessageIsResourceKey
                    ? Localize(result.Message, result.MessageArguments)
                    : result.Message,
                CloseButtonText = Localize("dialog.common.btn_ok")
            }.ShowDialogAsync();
        }

        RefreshStatus();
    }

    private static async Task<bool> ConfirmAsync(GameSettingConfirmation confirmation)
    {
        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = Localize(confirmation.TitleKey),
            Content = Localize(confirmation.MessageKey),
            PrimaryButtonText = Localize(confirmation.PrimaryButtonKey),
            CloseButtonText = Localize("dialog.common.btn_cancel")
        }.ShowDialogAsync();
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private void RefreshStatus()
    {
        if (_game is null || _integrationProvider is null)
        {
            return;
        }

        try
        {
            var status = _integrationProvider.GetStatus(_configuration, _game);
            _statusTextBlock.Text = Localize(status.MessageKey, status.MessageArguments);
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
            {
                button.IsEnabled = false;
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _statusRefreshTimer?.Start();
        RefreshStatus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _statusRefreshTimer?.Stop();
    }

    private static string Localize(string key, IReadOnlyList<object>? arguments = null)
    {
        var text = I18NLoader.Instance[key];
        return arguments is { Count: > 0 }
            ? string.Format(CultureInfo.CurrentCulture, text, arguments.ToArray())
            : text;
    }
}
