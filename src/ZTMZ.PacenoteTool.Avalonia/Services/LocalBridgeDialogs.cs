using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Avalonia.Services;

internal static class LocalBridgeDialogs
{
    private static readonly TaskCompletionSource WindowReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static void NotifyWindowReady() => WindowReady.TrySetResult();

    public static async Task<bool> ConfirmAsync(ILocalGameBridgeControl bridge, string? detail, Func<bool> isCurrent)
    {
        // FAAppWindow raises Opened after its startup splash has finished.
        await WindowReady.Task;
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (!isCurrent()) return false;
            var text = I18NLoader.Instance;
            var dialog = FaContentDialogFactory.Create(new FAContentDialog
            {
                Title = text[bridge.InjectionTitleKey],
                Content = new TextBlock
                {
                    Text = detail ?? string.Join(Environment.NewLine + Environment.NewLine,
                        text[bridge.InjectionWarningKey], text[bridge.InjectionPrivilegeWarningKey]),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520
                },
                PrimaryButtonText = text[detail is null ? "game.acr.experimental.launchPromptInject" : "game.acr.experimental.tryUnverified"],
                CloseButtonText = text["game.acr.experimental.launchPromptNotNow"],
                DefaultButton = FAContentDialogButton.Close
            });
            return await dialog.ShowAsync() == FAContentDialogResult.Primary;
        });
    }

    public static async Task ShowFailureAsync(ILocalGameBridgeControl bridge, string detail)
        => await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = FaContentDialogFactory.Create(new FAContentDialog
            {
                Title = I18NLoader.Instance[bridge.InjectionTitleKey],
                Content = new TextBlock
                {
                    Text = string.Format(I18NLoader.Instance["game.acr.experimental.launchPromptFailure"], detail),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520
                },
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            });
            await dialog.ShowAsync();
        });
}
