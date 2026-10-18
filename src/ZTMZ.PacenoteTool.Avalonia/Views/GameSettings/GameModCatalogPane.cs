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
using FluentAvalonia.UI.Controls;
using ZTMZ.PacenoteTool.Avalonia.Models;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Avalonia.Views.GameSettings;

public sealed class GameModCatalogPane : GameSettingsPane
{
    private readonly IGameWithModCatalog _capability;
    private readonly StackPanel _items = new() { Spacing = 8 };
    private readonly TextBlock _catalogStatus = new() { Opacity = 0.72, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _catalogProgress = new()
    {
        Minimum = 0,
        Maximum = 1,
        IsIndeterminate = true,
        IsVisible = false
    };
    private readonly GameModRepositoryClient _repositoryClient = new();
    private readonly Button _refreshButton;
    private readonly Border _updateBadge = CreateUpdateBadge();
    private IGame? _game;
    private IReadOnlyList<GameModDefinition> _mods = [];
    private CancellationTokenSource? _operationCancellation;
    private bool _loaded;
    private bool _isRefreshing;
    private bool _disposed;
    private readonly Dictionary<string, ModRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameModOperationResult> _operationResults = new(StringComparer.OrdinalIgnoreCase);

    public GameModCatalogPane(IGameWithModCatalog capability)
    {
        _capability = capability;
        var root = new StackPanel { Spacing = 5 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock
        {
            Text = Text("gameMods.title"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });
        header.Children.Add(_updateBadge);
        root.Children.Add(header);
        root.Children.Add(new TextBlock { Text = Text("gameMods.subtitle"), Opacity = 0.72 });
        _refreshButton = new Button
        {
            Content = Text("gameMods.action.refresh"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _refreshButton.Click += async (_, _) => await RefreshRowsAsync(reloadCatalog: true);
        root.Children.Add(_refreshButton);
        _catalogStatus.Text = Text("gameMods.loading");
        root.Children.Add(_catalogStatus);
        root.Children.Add(_catalogProgress);
        root.Children.Add(_items);
        Content = root;
        AttachedToVisualTree += async (_, _) => await LoadOnceAsync();
        DetachedFromVisualTree += (_, _) => _operationCancellation?.Cancel();
    }

    public override void InitializeWithGame(IGame game) => _game = game;

    private async Task LoadOnceAsync()
    {
        if (_loaded || _game is null)
            return;
        _loaded = true;
        await RefreshRowsAsync(reloadCatalog: true);
    }

    private async Task RefreshRowsAsync(bool reloadCatalog = false)
    {
        if (_disposed || _game is null || _isRefreshing || _operationCancellation is not null)
            return;

        _isRefreshing = true;
        _refreshButton.IsEnabled = false;
        _items.IsEnabled = false;
        var completionMessage = _catalogStatus.Text;
        _catalogStatus.Text = Text(reloadCatalog ? "gameMods.loading" : "gameMods.checkingInstalled");
        _catalogProgress.IsIndeterminate = true;
        _catalogProgress.IsVisible = true;
        try
        {
            var mods = _mods;
            if (reloadCatalog)
            {
                var result = await _repositoryClient.LoadAsync(
                    _capability.ModRepositoryUri, _capability.ModCatalogGameId, CancellationToken.None);
                completionMessage = result.Mods.Count == 0
                    ? Format("gameMods.catalogUnavailable", result.Error ?? string.Empty)
                    : result.IsCached ? Text("gameMods.cachedCatalog") : string.Empty;
                if (result.Mods.Count == 0 && result.Error is not null)
                {
                    _catalogStatus.Text = completionMessage;
                    return;
                }
                mods = result.Mods;
            }
            var rows = new List<ModRow>();
            int updates = 0;
            foreach (var mod in mods)
            {
                _catalogStatus.Text = Format("gameMods.checkingMod",
                    rows.Count + 1, mods.Count, mod.GetName(I18NLoader.Instance.CurrentCultureName));
                if (_rows.TryGetValue(mod.Id, out var existingRow))
                {
                    existingRow.Message.Text = Text("gameMods.checkingInstalled");
                    existingRow.Progress.IsIndeterminate = true;
                    existingRow.Operation.IsVisible = true;
                    existingRow.Progress.IsVisible = true;
                }
                GameModStatus status;
                try
                {
                    status = await _capability.ModManager.GetStatusAsync(
                        mod, mods, _game, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    status = new GameModStatus(GameModInstallState.Unavailable,
                        "gameMods.status.error", [exception.Message],
                        new HashSet<GameModActionKind> { GameModActionKind.OpenReleasePage });
                }
                if (_disposed)
                    return;
                rows.Add(CreateRow(mod, status));
                if (status.IsUpdateAvailable)
                    updates++;
            }
            _mods = mods;
            _items.Children.Clear();
            _rows.Clear();
            foreach (var row in rows)
            {
                _items.Children.Add(row.Card);
                _rows.Add(row.Id, row);
            }
            ((TextBlock)_updateBadge.Child!).Text = updates.ToString(CultureInfo.CurrentCulture);
            _updateBadge.IsVisible = updates > 0;
            _catalogStatus.Text = completionMessage;
        }
        catch (Exception exception)
        {
            _catalogStatus.Text = Format("gameMods.catalogUnavailable", exception.Message);
        }
        finally
        {
            _isRefreshing = false;
            _catalogProgress.IsVisible = false;
            foreach (var row in _rows.Values)
            {
                row.Progress.IsVisible = false;
                if (_operationResults.TryGetValue(row.Id, out var result))
                    ShowResult(row, result);
                else
                    row.Operation.IsVisible = false;
            }
            _refreshButton.IsEnabled = !_disposed && _operationCancellation is null;
            _items.IsEnabled = !_disposed && _operationCancellation is null;
        }
    }

    private ModRow CreateRow(GameModDefinition mod, GameModStatus status)
    {
        var content = new StackPanel { Spacing = 5 };
        var headingRow = new DockPanel();
        if (status.IsUpdateAvailable)
        {
            var badge = CreateUpdateBadge(Text("gameMods.status.updateAvailable"));
            DockPanel.SetDock(badge, Dock.Right);
            headingRow.Children.Add(badge);
        }
        headingRow.Children.Add(new TextBlock
        {
            Text = $"{mod.GetName(I18NLoader.Instance.CurrentCultureName)}  {mod.Version}",
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(headingRow);
        content.Children.Add(new TextBlock
        {
            Text = $"{mod.GetDescription(I18NLoader.Instance.CurrentCultureName)}\n{Format("gameMods.author", mod.Author)}",
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });
        if (mod.Credits is { Count: > 0 })
        {
            content.Children.Add(new Expander
            {
                Header = Text("gameMods.credits"),
                Content = new TextBlock
                {
                    Text = FormatCredits(mod),
                    Margin = new Thickness(12, 4, 0, 4),
                    Opacity = 0.72,
                    TextWrapping = TextWrapping.Wrap
                },
                IsExpanded = false
            });
        }

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        footer.Children.Add(new TextBlock
        {
            Text = Format(status.MessageKey, status.MessageArguments?.ToArray() ?? []),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var action in status.EnabledActions ?? new HashSet<GameModActionKind>())
        {
            var button = new Button
            {
                Content = Text(ActionKey(action)),
                Tag = new ModAction(mod, action),
                Margin = new Thickness(6, 0, 0, 0)
            };
            button.Click += RunAction;
            actions.Children.Add(button);
        }
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        content.Children.Add(footer);
        var operationText = new TextBlock { Name = "ModOperationStatus", TextWrapping = TextWrapping.Wrap };
        var progress = new ProgressBar
        {
            Name = "ModOperationProgress", Minimum = 0, Maximum = 1, Height = 4,
            Margin = new Thickness(0, 6, 0, 0), IsVisible = false
        };
        var cancel = new Button
        {
            Name = "ModOperationCancel", Content = Text("gameMods.action.cancel"),
            Margin = new Thickness(8, 0, 0, 0), IsVisible = false
        };
        var operationHeader = new DockPanel();
        DockPanel.SetDock(cancel, Dock.Right);
        operationHeader.Children.Add(cancel);
        operationHeader.Children.Add(operationText);
        var operation = new StackPanel { Margin = new Thickness(0, 8, 0, 0), IsVisible = false };
        operation.Children.Add(operationHeader);
        operation.Children.Add(progress);
        content.Children.Add(operation);
        var card = new Border
        {
            Child = content,
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, 128, 128, 128)),
            CornerRadius = new CornerRadius(6)
        };
        card.Bind(Border.BackgroundProperty, new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("CardBackgroundFillColorDefaultBrush"));
        var row = new ModRow(mod.Id, card, actions, operation, operationText, progress, cancel);
        if (_operationResults.TryGetValue(mod.Id, out var result))
            ShowResult(row, result);
        return row;
    }

    private async void RunAction(object? sender, RoutedEventArgs eventArgs)
    {
        if (_disposed || _isRefreshing || _operationCancellation is not null ||
            _game is null || sender is not Button { Tag: ModAction request })
            return;
        if (request.Action != GameModActionKind.OpenReleasePage && !await ConfirmAsync(request))
            return;

        await ExecuteActionAsync(request);
    }

    private async Task ExecuteActionAsync(ModAction request)
    {
        // Confirmation can yield to another click or navigation away from this pane.
        if (_disposed || _isRefreshing || _operationCancellation is not null ||
            _game is null || !_rows.TryGetValue(request.Mod.Id, out var row))
            return;

        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        _operationResults.Remove(request.Mod.Id);
        SetEnabled(false);
        row.Message.Text = Text("gameMods.progress.preparing");
        row.Operation.IsVisible = true;
        row.Progress.Value = 0;
        row.Progress.IsIndeterminate = true;
        row.Progress.IsVisible = true;
        row.Cancel.IsEnabled = true;
        row.Cancel.IsVisible = true;
        var acceptingProgress = true;
        void Cancel(object? sender, RoutedEventArgs args)
        {
            row.Cancel.IsEnabled = false;
            row.Message.Text = Text("gameMods.progress.cancelling");
            row.Progress.IsIndeterminate = true;
            cancellation.Cancel();
        }
        row.Cancel.Click += Cancel;
        var progress = new Progress<GameModOperationProgress>(value =>
        {
            // Queued progress must not overwrite completion, cancellation or a later operation.
            if (!acceptingProgress || _disposed || cancellation.IsCancellationRequested ||
                !ReferenceEquals(_operationCancellation, cancellation))
                return;
            row.Message.Text = Format(value.MessageKey, value.MessageArguments?.ToArray() ?? []);
            row.Progress.IsIndeterminate = !value.Fraction.HasValue;
            if (value.Fraction.HasValue)
            {
                row.Progress.Value = Math.Clamp(value.Fraction.Value, 0, 1);
                row.Message.Text += " " + row.Progress.Value.ToString("P0", CultureInfo.CurrentCulture);
            }
        });
        GameModOperationResult result;
        try
        {
            if (request.Action == GameModActionKind.OpenReleasePage)
            {
                if (!await DesktopUriLauncher.TryOpenAsync(request.Mod.ReleasePageUri))
                    throw new InvalidOperationException(Text("gameMods.result.openReleaseFailed"));
                result = new GameModOperationResult(true, "gameMods.result.openedByUi");
            }
            else
            {
                result = await _capability.ModManager.ExecuteActionAsync(
                    request.Mod, _mods, request.Action, _game, progress, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            result = new GameModOperationResult(false, "gameMods.result.cancelled");
        }
        catch (Exception exception)
        {
            result = new GameModOperationResult(false, exception.Message, MessageIsResourceKey: false);
        }
        finally
        {
            acceptingProgress = false;
            row.Cancel.Click -= Cancel;
            _operationCancellation = null;
            row.Progress.IsVisible = false;
            row.Cancel.IsVisible = false;
            SetEnabled(true);
        }
        _operationResults[request.Mod.Id] = result;
        ShowResult(row, result);
        if (!_disposed && request.Action != GameModActionKind.OpenReleasePage)
            await RefreshRowsAsync();
    }

    private static void ShowResult(ModRow row, GameModOperationResult result)
    {
        row.Message.Text = result.MessageIsResourceKey
            ? Format(result.Message, result.MessageArguments?.ToArray() ?? [])
            : result.Message;
        row.Operation.IsVisible = true;
    }

    private async Task<bool> ConfirmAsync(ModAction request)
    {
        var dialog = FaContentDialogFactory.Create(new FAContentDialog
        {
            Title = Text("gameMods.title"),
            Content = Format("gameMods.confirm", Text(ActionKey(request.Action)),
                request.Mod.GetName(I18NLoader.Instance.CurrentCultureName)),
            PrimaryButtonText = Text(ActionKey(request.Action)),
            CloseButtonText = Text("dialog.common.btn_cancel"),
            DefaultButton = FAContentDialogButton.Primary
        });
        return await dialog.ShowAsync() == FAContentDialogResult.Primary;
    }

    private void SetEnabled(bool enabled)
    {
        _refreshButton.IsEnabled = enabled && !_isRefreshing && !_disposed;
        foreach (var row in _rows.Values)
            row.Actions.IsEnabled = enabled && !_disposed;
    }

    private static string ActionKey(GameModActionKind action) => action switch
    {
        GameModActionKind.Install => "gameMods.action.install",
        GameModActionKind.Update => "gameMods.action.update",
        GameModActionKind.Uninstall => "gameMods.action.uninstall",
        GameModActionKind.AddToExternalManager => "gameMods.action.addToJsgme",
        GameModActionKind.OpenExternalManager => "gameMods.action.openJsgme",
        _ => "gameMods.action.openRelease"
    };

    private static string Text(string key) => I18NLoader.Instance[key];
    private static Border CreateUpdateBadge(string? label = null)
    {
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(209, 52, 56)),
            CornerRadius = new CornerRadius(10),
            MinWidth = 20,
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = label is not null,
            Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center }
        };
        ToolTip.SetTip(badge, Text("gameMods.status.updateAvailable"));
        return badge;
    }
    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Text(key), arguments);
    private static string FormatCredits(GameModDefinition mod)
    {
        var culture = I18NLoader.Instance.CurrentCultureName;
        var separator = culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "、" : ", ";
        var delimiter = culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "：" : ": ";
        return string.Join(Environment.NewLine, mod.Credits!.Select(credit =>
            credit.GetRole(culture) + delimiter + string.Join(separator, credit.Names)));
    }

    public override void Dispose()
    {
        _disposed = true;
        // The executing method owns disposal; cancel without invalidating its token.
        _operationCancellation?.Cancel();
        base.Dispose();
    }

    private sealed record ModRow(string Id, Border Card, Panel Actions, StackPanel Operation,
        TextBlock Message, ProgressBar Progress, Button Cancel);

    private sealed record ModAction(GameModDefinition Mod, GameModActionKind Action);
}
