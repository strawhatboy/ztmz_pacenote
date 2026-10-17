using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Views.Dialog
{
    /// <summary>
    /// Interaction logic for NewUpdateDialog.xaml
    /// </summary>
    public partial class NewUpdateDialog : ContentDialog
    {
        private string _newVersion;
        private readonly ContentDialogHost _dialogHost;
        public NewUpdateDialog(ContentDialogHost dialogHost, string newVersion, string currentVersion, string changelogUrl) : base(dialogHost)
        {
            _dialogHost = dialogHost;
            _newVersion = newVersion;
            InitializeComponent();
            this.tb_content.Text = string.Format(I18NLoader.Instance["dialog.newUpdate.content"],
                newVersion, currentVersion);
            this.wb_Changelog.Navigate(new Uri(changelogUrl)); 
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _dialogHost.SizeChanged += OnHostSizeChanged;
            UpdateDialogSize();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) =>
            _dialogHost.SizeChanged -= OnHostSizeChanged;

        private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDialogSize();

        private void UpdateDialogSize()
        {
            if (_dialogHost.ActualWidth > 0 && _dialogHost.ActualHeight > 0)
            {
                DialogWidth = Math.Min(960, Math.Max(0, _dialogHost.ActualWidth - 48));
                DialogHeight = Math.Min(680, Math.Max(0, _dialogHost.ActualHeight - 48));
                // ContentDialog measures its body inside a ScrollViewer, so a
                // star row alone leaves WebBrowser at its 150-DIP default height.
                // Reserve space for the title, action buttons and template padding.
                ChangelogLayout.Height = Math.Max(0, DialogHeight - 200);
            }
        }

        private void chkbox_skip_Click(object sender, RoutedEventArgs e) 
        {
            if (this.chkbox_skip != null)
            {
                if (this.chkbox_skip.IsChecked.HasValue && this.chkbox_skip.IsChecked.Value)
                {
                    Config.Instance.SkippedVersion = _newVersion;
                    Config.Instance.SaveUserConfig();
                }
            }
        }
    }
}
