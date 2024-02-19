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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ZTMZ.PacenoteTool.Dialog
{
    /// <summary>
    /// Interaction logic for ResetConfigDialog.xaml
    /// </summary>
    public partial class ResetConfigDialog : Window
    {
        public ResetConfigDialog()
        {
            InitializeComponent();
            this.MouseDown += (o, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
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
