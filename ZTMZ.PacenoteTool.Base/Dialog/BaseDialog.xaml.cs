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

namespace ZTMZ.PacenoteTool.Base.Dialog
{
    /// <summary>
    /// Interaction logic for BaseDialog.xaml
    /// </summary>
    public partial class BaseDialog : Window
    {
        public BaseDialog(params object[] args)
        {
            InitializeComponent();
            this.tb_Content.Text = string.Format(I18NLoader.Instance["dialog.portMismatch.content"], args);
            this.MouseDown += delegate { DragMove(); };
        }

        private void btn_ok_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void btn_FORCE_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            if (this.chkbox_show != null)
            {
                if (this.chkbox_show.IsChecked.HasValue && this.chkbox_show.IsChecked.Value)
                {
                    Config.Instance.WarnIfPortMismatch = false;
                    Config.Instance.SaveUserConfig();
                }
            }
        }
    }
}
