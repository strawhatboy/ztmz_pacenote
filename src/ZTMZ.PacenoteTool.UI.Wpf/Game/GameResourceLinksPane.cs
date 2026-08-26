#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.UI.Windows.Game;

public sealed class GameResourceLinksPane : UserControl
{
    public GameResourceLinksPane(IReadOnlyList<GameResourceLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        var root = new UniformGrid
        {
            Columns = Math.Min(2, Math.Max(1, links.Count))
        };

        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            var card = new CardAction
            {
                Padding = new Thickness(16),
                Margin = index == 0
                    ? new Thickness(0, 0, links.Count > 1 ? 10 : 0, 0)
                    : new Thickness(10, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                BorderThickness = new Thickness(2),
                IsChevronVisible = false,
                Tag = link
            };
            card.SetResourceReference(
                Control.BorderBrushProperty,
                link.Kind == GameResourceKind.Pacenotes
                    ? "SystemAccentColorPrimaryBrush"
                    : "SystemFillColorSuccessBrush");

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new Wpf.Ui.Controls.TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            label.SetResourceReference(Wpf.Ui.Controls.TextBlock.TextProperty, link.LabelKey);
            text.Children.Add(label);

            if (!string.IsNullOrWhiteSpace(link.DescriptionKey))
            {
                var description = new Wpf.Ui.Controls.TextBlock
                {
                    Margin = new Thickness(0, 4, 12, 0),
                    Opacity = 0.72,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                description.SetResourceReference(
                    Wpf.Ui.Controls.TextBlock.TextProperty,
                    link.DescriptionKey);
                description.SetResourceReference(
                    ToolTipService.ToolTipProperty,
                    link.DescriptionKey);
                ToolTipService.SetInitialShowDelay(description, 100);
                text.Children.Add(description);
            }
            content.Children.Add(text);

            var icon = new SymbolIcon
            {
                FontSize = 26,
                Symbol = link.Kind == GameResourceKind.Pacenotes
                    ? SymbolRegular.Script16
                    : SymbolRegular.LocalLanguage20
            };
            Grid.SetColumn(icon, 1);
            content.Children.Add(icon);

            if (!string.IsNullOrWhiteSpace(link.ToolTipKey))
            {
                card.SetResourceReference(ToolTipService.ToolTipProperty, link.ToolTipKey);
                ToolTipService.SetInitialShowDelay(card, 100);
            }

            card.Content = content;
            card.Click += OpenLink;
            root.Children.Add(card);
        }

        Content = root;
    }

    private static void OpenLink(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not CardAction { Tag: GameResourceLink link } ||
            !link.Uri.IsAbsoluteUri ||
            (link.Uri.Scheme != Uri.UriSchemeHttps && link.Uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(link.Uri.AbsoluteUri) { UseShellExecute = true });
    }
}
