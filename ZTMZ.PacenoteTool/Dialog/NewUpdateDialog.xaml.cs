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
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Dialog
{
    /// <summary>
    /// Interaction logic for NewUpdateDialog.xaml
    /// </summary>
    public partial class NewUpdateDialog : Window
    {
        public NewUpdateDialog(string newVersion, string currentVersion, string changelogUrl)
        {
            InitializeComponent();
            this.tb_content.Text = string.Format(I18NLoader.Instance.CurrentDict["dialog.newUpdate.content"].ToString(),
                newVersion, currentVersion);
            this.wb_Changelog.Navigate(new Uri(changelogUrl));
        }


        private void btn_ok_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void btn_cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
