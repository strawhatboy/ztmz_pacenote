using System;
using System.Threading.Tasks;
using System.Windows;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

internal static class OverlayInjectionWarning
{
    public static Task<bool> ShowAsync(Func<bool> isCurrent) =>
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (!isCurrent()) return false;
            var text = I18NLoader.Instance;
            return await new Wpf.Ui.Controls.MessageBox
            {
                Title = text["overlayBackend.warningTitle"],
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = text["overlayBackend.warningBody"],
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520
                },
                PrimaryButtonText = text["overlayBackend.warningKeepWindow"],
                SecondaryButtonText = text["overlayBackend.warningEnable"],
                CloseButtonText = text["dialog.common.btn_cancel"]
            }.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Secondary;
        }).Task.Unwrap();
}
