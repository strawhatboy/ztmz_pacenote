#nullable enable

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ZTMZ.PacenoteTool.Avalonia.Models;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Avalonia.Views.GameSettings;

public sealed class GameResourceLinksPane : GameSettingsPane
{
    public GameResourceLinksPane(IReadOnlyList<GameResourceLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        var root = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var link in links)
        {
            var text = new StackPanel { Spacing = 3 };
            text.Children.Add(new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                Text = I18NLoader.Instance[link.LabelKey],
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(link.DescriptionKey))
            {
                var descriptionText = I18NLoader.Instance[link.DescriptionKey];
                var description = new TextBlock
                {
                    FontSize = 12,
                    Opacity = 0.72,
                    Text = descriptionText,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                ToolTip.SetTip(description, descriptionText);
                ToolTip.SetShowDelay(description, 100);
                text.Children.Add(description);
            }

            var button = new Button
            {
                Margin = new Thickness(8, 4, 0, 0),
                Padding = new Thickness(14, 8),
                MinWidth = 280,
                MaxWidth = 520,
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(link.Kind == GameResourceKind.Pacenotes
                    ? Color.Parse("#0078D4")
                    : Color.Parse("#107C10")),
                Tag = link,
                Content = text,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            if (!string.IsNullOrWhiteSpace(link.ToolTipKey))
            {
                ToolTip.SetTip(button, I18NLoader.Instance[link.ToolTipKey]);
                ToolTip.SetShowDelay(button, 100);
            }
            button.Click += OpenLink;
            root.Children.Add(button);
        }

        Content = root;
    }

    public override void InitializeWithGame(IGame game) { }

    private static async void OpenLink(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: GameResourceLink link })
            await DesktopUriLauncher.TryOpenAsync(link.Uri);
    }
}
