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
    /// Interaction logic for PortMismatchDialog.xaml
    /// </summary>
    public partial class PortMismatchDialog : Window
    {
        public PortMismatchDialog(string port1, string port2)
        {
            InitializeComponent();
            this.tb_Content.Text = string.Format(I18NLoader.Instance.CurrentDict["dialog.portMismatch.content"].ToString(), port1, port2);
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
    }
}
