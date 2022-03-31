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
    /// Interaction logic for EnableGoogleAnalyticsDialog.xaml
    /// </summary>
    public partial class EnableGoogleAnalyticsDialog : Window
    {
        private bool buttonClicked;
        public EnableGoogleAnalyticsDialog()
        {
            InitializeComponent();
            // this.tb_Content.Text = string.Format(I18NLoader.Instance["dialog.enableGoogleAnalytics.content"], port1, port2);
            this.MouseDown += delegate { DragMove(); };
            this.Closing += (s, e) => {
                if (!buttonClicked) {
                    e.Cancel = true;
                }
            };
        }

        private void btn_ok_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            buttonClicked = true;
            this.Close();
        }

        private void btn_cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            buttonClicked = true;
            this.Close();
        }
    }
}
