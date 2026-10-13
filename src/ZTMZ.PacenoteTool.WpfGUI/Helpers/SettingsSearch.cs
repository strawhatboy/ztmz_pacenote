using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZTMZ.PacenoteTool.WpfGUI.Helpers;

public static class SettingsSearch
{
    public static readonly DependencyProperty AnchorProperty = DependencyProperty.RegisterAttached(
        "Anchor", typeof(string), typeof(SettingsSearch), new PropertyMetadata(null));
    public static void SetAnchor(DependencyObject element, string value) => element.SetValue(AnchorProperty, value);
    public static string GetAnchor(DependencyObject element) => (string)element.GetValue(AnchorProperty);

    internal static FrameworkElement Find(DependencyObject root, string anchor)
    {
        var visited = new HashSet<DependencyObject>();
        return Walk(root);
        FrameworkElement Walk(DependencyObject node)
        {
            if (!visited.Add(node)) return null;
            if (node is FrameworkElement element && GetAnchor(element) == anchor) return element;
            // Logical children include collapsed expander content that is not in the visual tree yet.
            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject dependency && Walk(dependency) is { } found) return found;
            if (node is Visual)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    if (Walk(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
            return null;
        }
    }

    internal static void Reveal(FrameworkElement target)
    {
        for (DependencyObject parent = target; parent != null; parent =
            LogicalTreeHelper.GetParent(parent) ?? (parent is Visual ? VisualTreeHelper.GetParent(parent) : null))
        {
            if (parent is Expander expander) expander.SetCurrentValue(Expander.IsExpandedProperty, true);
            if (parent is Wpf.Ui.Controls.CardExpander card) card.SetCurrentValue(Wpf.Ui.Controls.CardExpander.IsExpandedProperty, true);
        }
        target.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            target.BringIntoView();
            var layer = AdornerLayer.GetAdornerLayer(target);
            if (layer == null) return;
            var adorner = new SearchHighlight(target);
            layer.Add(adorner);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { timer.Stop(); layer.Remove(adorner); };
            timer.Start();
        }));
    }

    private sealed class SearchHighlight(UIElement target) : Adorner(target)
    {
        protected override void OnRender(DrawingContext context)
        {
            IsHitTestVisible = false;
            context.DrawRoundedRectangle(null, new Pen(Brushes.Goldenrod, 2), new Rect(AdornedElement.RenderSize), 4, 4);
        }
    }
}
