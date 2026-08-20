#nullable enable

using Avalonia;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

/// <summary>Creates FluentAvalonia dialogs using the interface typography selected in General settings.</summary>
internal static class FaContentDialogFactory
{
    public static FAContentDialog Create()
    {
        var dialog = new FAContentDialog();
        ApplyTypography(dialog);
        return dialog;
    }

    public static FAContentDialog Create(FAContentDialog dialog)
    {
        ApplyTypography(dialog);
        return dialog;
    }

    public static void ApplyTypography(FAContentDialog dialog)
    {
        if (Application.Current?.Resources["AppFontFamily"] is FontFamily fontFamily)
        {
            dialog.FontFamily = fontFamily;
        }

        if (Application.Current?.Resources["AppFontSize"] is double fontSize)
        {
            dialog.FontSize = fontSize;
        }
    }
}
