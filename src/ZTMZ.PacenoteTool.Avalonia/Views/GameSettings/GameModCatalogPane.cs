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
    private readonly GameModRepositoryClient _repositoryClient = new();
    private readonly Button _cancelButton;
    private readonly Button _refreshButton;
    private IGame? _game;
    private IReadOnlyList<GameModDefinition> _mods = [];
    private CancellationTokenSource? _operationCancellation;
    private bool _loaded;
    private bool _isRefreshing;

    public GameModCatalogPane(IGameWithModCatalog capability)
    {
        _capability = capability;
        var root = new StackPanel { Spacing = 5 };
        root.Children.Add(new TextBlock
        {
            Text = Text("gameMods.title"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });
        root.Children.Add(new TextBlock { Text = Text("gameMods.subtitle"), Opacity = 0.72 });
        _refreshButton = new Button
        {
            Content = Text("gameMods.action.refresh"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _refreshButton.Click += async (_, _) => await RefreshRowsAsync();
        root.Children.Add(_refreshButton);
        _catalogStatus.Text = Text("gameMods.loading");
        root.Children.Add(_catalogStatus);
        _cancelButton = new Button
        {
            Content = Text("gameMods.action.cancel"),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsVisible = false
        };
        _cancelButton.Click += (_, _) => _operationCancellation?.Cancel();
        root.Children.Add(_cancelButton);
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
        var result = await _repositoryClient.LoadAsync(
            _capability.ModRepositoryUri, _capability.ModCatalogGameId, CancellationToken.None);
        _mods = result.Mods;
        _catalogStatus.Text = _mods.Count == 0
            ? Format("gameMods.catalogUnavailable", result.Error ?? string.Empty)
            : result.IsCached ? Text("gameMods.cachedCatalog") : string.Empty;
        await RefreshRowsAsync();
    }

    private async Task RefreshRowsAsync()
    {
        if (_game is null || _isRefreshing || _operationCancellation is not null)
            return;

        _isRefreshing = true;
        _refreshButton.IsEnabled = false;
        try
        {
            _items.Children.Clear();
            foreach (var mod in _mods)
            {
                var status = await _capability.ModManager.GetStatusAsync(
                    mod, _mods, _game, CancellationToken.None);
                _items.Children.Add(CreateRow(mod, status));
            }
        }
        finally
        {
            _isRefreshing = false;
            _refreshButton.IsEnabled = _operationCancellation is null;
        }
    }

    private Control CreateRow(GameModDefinition mod, GameModStatus status)
    {
        var content = new StackPanel { Spacing = 5 };
        content.Children.Add(new TextBlock
        {
            Text = $"{mod.GetName(I18NLoader.Instance.CurrentCultureName)}  {mod.Version}",
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
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
        return new Border
        {
            Child = content,
            Padding = new Thickness(14),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, 128, 128, 128)),
            CornerRadius = new CornerRadius(4)
        };
    }

    private async void RunAction(object? sender, RoutedEventArgs eventArgs)
    {
        if (_game is null || sender is not Button { Tag: ModAction request })
            return;
        if (request.Action == GameModActionKind.OpenReleasePage)
        {
            await DesktopUriLauncher.TryOpenAsync(request.Mod.ReleasePageUri);
            return;
        }
        if (!await ConfirmAsync(request))
            return;

        _items.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        _cancelButton.IsVisible = true;
        _operationCancellation = new CancellationTokenSource();
        var progress = new Progress<GameModOperationProgress>(value =>
            _catalogStatus.Text = Format(value.MessageKey, value.MessageArguments?.ToArray() ?? []));
        var result = await _capability.ModManager.ExecuteActionAsync(
            request.Mod, _mods, request.Action, _game, progress, _operationCancellation.Token);
        _operationCancellation.Dispose();
        _operationCancellation = null;
        _items.IsEnabled = true;
        _refreshButton.IsEnabled = true;
        _cancelButton.IsVisible = false;
        _catalogStatus.Text = result.MessageIsResourceKey
            ? Format(result.Message, result.MessageArguments?.ToArray() ?? [])
            : result.Message;
        await RefreshRowsAsync();
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
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        base.Dispose();
    }

    private sealed record ModAction(GameModDefinition Mod, GameModActionKind Action);
}
