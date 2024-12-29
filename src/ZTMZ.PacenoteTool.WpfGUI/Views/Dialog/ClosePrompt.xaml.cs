using System.Windows.Controls;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Views.Dialog
{
    /// <summary>
    /// Interaction logic for DownloadFileDialog.xaml
    /// </summary>
    public partial class ClosePrompt : ContentDialog
    {
        public bool CloseToMinimize => rb_closeToMinimize.IsChecked ?? false;
        public ClosePrompt(ContentPresenter contentPresenter)
            : base(contentPresenter)
        {
            InitializeComponent();

            if (Config.Instance.CloseWindowToMinimize) {
                rb_closeToMinimize.IsChecked = true;
                rb_closeToExit.IsChecked = false;
            } else {
                rb_closeToMinimize.IsChecked = false;
                rb_closeToExit.IsChecked = true;
            }
        }
    }
}
