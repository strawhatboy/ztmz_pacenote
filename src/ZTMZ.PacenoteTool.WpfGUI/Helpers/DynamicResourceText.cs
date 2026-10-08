using System.Windows;
using System.Windows.Controls;

namespace ZTMZ.PacenoteTool.WpfGUI.Helpers;

/// <summary>Binds a changing localization key while preserving dynamic language updates.</summary>
public static class DynamicResourceText
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(DynamicResourceText), new PropertyMetadata(null, OnKeyChanged));

    public static string GetKey(DependencyObject target) => (string)target.GetValue(KeyProperty);
    public static void SetKey(DependencyObject target, string value) => target.SetValue(KeyProperty, value);

    private static void OnKeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBlock text) return;
        if (args.NewValue is string key && !string.IsNullOrWhiteSpace(key))
            text.SetResourceReference(TextBlock.TextProperty, key);
        else
            text.ClearValue(TextBlock.TextProperty);
    }
}
