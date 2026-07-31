using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Views.Dialog;

public partial class DataFolderMoveConfirmationDialog : ContentDialog
{
    public DataFolderMoveConfirmationDialog(
        ContentDialogHost dialogHost,
        DataFolderMigrationPlan plan,
        string dataFolderSize,
        string requiredFreeSpace,
        string availableFreeSpace)
        : base(dialogHost)
    {
        InitializeComponent();

        SummaryText.Text = string.Format(
            I18NLoader.Instance["dataFolderMigration.confirmation"],
            plan.SourcePath,
            plan.DestinationPath,
            dataFolderSize,
            requiredFreeSpace,
            availableFreeSpace);
    }
}
