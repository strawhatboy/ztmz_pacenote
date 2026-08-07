using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;

public partial class SystemFontWarningDialog : ContentDialog
{
    public SystemFontWarningDialog(ContentDialogHost dialogHost, string fontName)
        : base(dialogHost)
    {
        InitializeComponent();
        WarningText.Text = string.Format(
            I18NLoader.Instance["font.systemFontWarningContent"],
            fontName);
    }
}
