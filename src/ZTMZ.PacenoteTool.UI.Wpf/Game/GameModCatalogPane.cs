#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.UI.Windows.Game;

public sealed class GameModCatalogPane : UserControl, IDisposable
{
    private readonly IGame _game;
    private readonly IGameWithModCatalog _capability;
    private readonly StackPanel _items = new();
    private readonly TextBlock _catalogStatus = new() { Opacity = 0.72, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _operationProgress = new()
    {
        Minimum = 0,
        Maximum = 1,
        IsIndeterminate = true,
        Visibility = Visibility.Collapsed,
        Height = 4,
        Margin = new Thickness(0, 5, 0, 7)
    };
    private readonly GameModRepositoryClient _repositoryClient = new();
    private readonly Button _refreshButton;
    private readonly Border _updateBadge = CreateUpdateBadge();
    private CancellationTokenSource? _operationCancellation;
    private IReadOnlyList<GameModDefinition> _mods = [];
    private bool _loaded;
    private bool _isRefreshing;

    public GameModCatalogPane(IGame game, IGameWithModCatalog capability)
    {
        _game = game;
        _capability = capability;

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = Text("gameMods.subtitle"),
            Margin = new Thickness(0, 0, 0, 10),
            Opacity = 0.72
        });
        _refreshButton = new Button
        {
            Content = Text("gameMods.action.refresh"),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _refreshButton.Click += async (_, _) => await RefreshRowsAsync(reloadCatalog: true);
        root.Children.Add(_refreshButton);
        _catalogStatus.Text = Text("gameMods.loading");
        root.Children.Add(_catalogStatus);
        root.Children.Add(_operationProgress);
        root.Children.Add(_items);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.PuzzleCube24,
            FontSize = 26,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var headerText = new TextBlock
        {
            Text = Text("gameMods.title"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerText.SetResourceReference(TextBlock.FontSizeProperty, "AppBodyStrongFontSize");
        header.Children.Add(headerText);
        header.Children.Add(_updateBadge);
        Content = new Wpf.Ui.Controls.CardExpander
        {
            Header = header,
            Content = root,
            IsExpanded = false,
            Padding = new Thickness(16)
        };
        Loaded += OnLoaded;
        Unloaded += (_, _) => _operationCancellation?.Cancel();
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_loaded)
            return;
        _loaded = true;
        await RefreshRowsAsync(reloadCatalog: true);
    }

    private async Task RefreshRowsAsync(bool reloadCatalog = false)
    {
        if (_isRefreshing || _operationCancellation is not null)
            return;

        _isRefreshing = true;
        _refreshButton.IsEnabled = false;
        _items.IsEnabled = false;
        try
        {
            var mods = _mods;
            if (reloadCatalog)
            {
                var result = await _repositoryClient.LoadAsync(
                    _capability.ModRepositoryUri, _capability.ModCatalogGameId, CancellationToken.None);
                _catalogStatus.Text = result.Mods.Count == 0
                    ? Format("gameMods.catalogUnavailable", result.Error ?? string.Empty)
                    : result.IsCached ? Text("gameMods.cachedCatalog") : string.Empty;
                if (result.Mods.Count == 0 && result.Error is not null)
                    return;
                mods = result.Mods;
            }
            var rows = new List<FrameworkElement>();
            int updates = 0;
            foreach (var mod in mods)
            {
                var status = await _capability.ModManager.GetStatusAsync(
                    mod, mods, _game, CancellationToken.None);
                rows.Add(CreateRow(mod, status));
                if (status.IsUpdateAvailable)
                    updates++;
            }
            _mods = mods;
            _items.Children.Clear();
            foreach (var row in rows)
                _items.Children.Add(row);
            ((TextBlock)_updateBadge.Child).Text = updates.ToString(CultureInfo.CurrentCulture);
            _updateBadge.Visibility = updates > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            _catalogStatus.Text = Format("gameMods.catalogUnavailable", exception.Message);
        }
        finally
        {
            _isRefreshing = false;
            _refreshButton.IsEnabled = _operationCancellation is null;
            _items.IsEnabled = _operationCancellation is null;
        }
    }

    private FrameworkElement CreateRow(GameModDefinition mod, GameModStatus status)
    {
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition());

        var heading = new TextBlock
        {
            Text = $"{mod.GetName(I18NLoader.Instance.CurrentCultureName)}  {mod.Version}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        var headingRow = new DockPanel();
        if (status.IsUpdateAvailable)
        {
            var badge = CreateUpdateBadge(Text("gameMods.status.updateAvailable"));
            DockPanel.SetDock(badge, Dock.Right);
            headingRow.Children.Add(badge);
        }
        headingRow.Children.Add(heading);
        body.Children.Add(headingRow);

        var description = new TextBlock
        {
            Text = $"{mod.GetDescription(I18NLoader.Instance.CurrentCultureName)}\n{Format("gameMods.author", mod.Author)}",
            Margin = new Thickness(0, 5, 0, 7),
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(description, 1);
        body.Children.Add(description);

        var footerRow = 2;
        if (mod.Credits is { Count: > 0 })
        {
            body.RowDefinitions.Add(new RowDefinition());
            var credits = new Expander
            {
                Header = Text("gameMods.credits"),
                Content = new TextBlock
                {
                    Text = FormatCredits(mod),
                    Margin = new Thickness(12, 4, 0, 4),
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap
                },
                Margin = new Thickness(0, 0, 0, 7),
                IsExpanded = false
            };
            Grid.SetRow(credits, 2);
            body.Children.Add(credits);
            footerRow = 3;
        }

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusText = new TextBlock
        {
            Text = Format(status.MessageKey, status.MessageArguments?.ToArray() ?? []),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        footer.Children.Add(statusText);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var action in status.EnabledActions ?? new HashSet<GameModActionKind>())
        {
            var button = new Button
            {
                Content = Text(ActionKey(action)),
                Tag = new ModAction(mod, action),
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(12, 5, 12, 5)
            };
            button.Click += RunAction;
            actions.Children.Add(button);
        }
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, footerRow);
        body.Children.Add(footer);

        return new Border
        {
            Child = body,
            Padding = new Thickness(0, 10, 0, 10),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, 128, 128, 128))
        };
    }

    private async void RunAction(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: ModAction request })
            return;
        if (request.Action == GameModActionKind.OpenReleasePage)
        {
            Process.Start(new ProcessStartInfo(request.Mod.ReleasePageUri.AbsoluteUri) { UseShellExecute = true });
            return;
        }
        if (!Confirm(request))
            return;

        SetEnabled(false);
        _operationProgress.Value = 0;
        _operationProgress.IsIndeterminate = true;
        _operationProgress.Visibility = Visibility.Visible;
        _operationCancellation = new CancellationTokenSource();
        var progress = new Progress<GameModOperationProgress>(value =>
        {
            _catalogStatus.Text = Format(value.MessageKey, value.MessageArguments?.ToArray() ?? []);
            _operationProgress.IsIndeterminate = !value.Fraction.HasValue;
            if (value.Fraction.HasValue)
                _operationProgress.Value = Math.Clamp(value.Fraction.Value, 0, 1);
        });
        var cancel = new Button
        {
            Content = Text("gameMods.action.cancel"),
            Margin = new Thickness(0, 6, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        cancel.Click += (_, _) => _operationCancellation?.Cancel();
        _items.Children.Insert(0, cancel);

        GameModOperationResult result;
        try
        {
            result = await _capability.ModManager.ExecuteActionAsync(
                request.Mod, _mods, request.Action, _game, progress, _operationCancellation.Token);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            _operationProgress.Visibility = Visibility.Collapsed;
            SetEnabled(true);
        }
        _catalogStatus.Text = result.MessageIsResourceKey
            ? Format(result.Message, result.MessageArguments?.ToArray() ?? [])
            : result.Message;
        await RefreshRowsAsync();
    }

    private bool Confirm(ModAction request)
    {
        var result = MessageBox.Show(
            Format("gameMods.confirm", Text(ActionKey(request.Action)),
                request.Mod.GetName(I18NLoader.Instance.CurrentCultureName)),
            Text("gameMods.title"),
            MessageBoxButton.OKCancel,
            request.Action == GameModActionKind.Uninstall ? MessageBoxImage.Warning : MessageBoxImage.Question);
        return result == MessageBoxResult.OK;
    }

    private void SetEnabled(bool enabled)
    {
        _refreshButton.IsEnabled = enabled && !_isRefreshing;
        foreach (var button in FindVisualChildren<Button>(_items))
            button.IsEnabled = enabled;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
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
    private static Border CreateUpdateBadge(string? label = null) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(209, 52, 56)),
        CornerRadius = new CornerRadius(10),
        MinWidth = 20,
        Padding = new Thickness(6, 1, 6, 1),
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = label is null ? Visibility.Collapsed : Visibility.Visible,
        ToolTip = Text("gameMods.status.updateAvailable"),
        Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center }
    };
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

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
    }

    private sealed record ModAction(GameModDefinition Mod, GameModActionKind Action);
}
