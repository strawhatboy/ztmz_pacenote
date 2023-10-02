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

// using Wpf.Ui.Controls;
using System.ComponentModel;
using Wpf.Ui;

namespace ZTMZ.PacenoteTool.Base.UI.Dialog
{
    /// <summary>
    /// Interaction logic for BaseDialog.xaml
    /// </summary>
    public partial class BaseDialog : Wpf.Ui.Controls.ContentDialog
    {
        public MessageBoxResult Result { get; set; }
        public BaseDialog(ContentPresenter contentPresenter, string title_id, string msg_id, object[] args, MessageBoxButton button, MessageBoxImage image) : base(contentPresenter) {
            
            InitializeComponent();
            args = args == null ? new object[] { } : args;
            this.tb_Title.Text = string.Format(I18NLoader.Instance[title_id], args);
            this.tb_Content.Text = string.Format(I18NLoader.Instance[msg_id], args);
            switch (button) 
            {
                case MessageBoxButton.OK:
                    this.btn_OK.IsDefault = true;
                    this.btn_OK.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.OKCancel:
                    this.btn_OK.IsDefault = true;
                    this.btn_OK.Visibility = Visibility.Visible;
                    this.btn_Cancel.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNo:
                    this.btn_Yes.IsDefault = true;
                    this.btn_Yes.Visibility = Visibility.Visible;
                    this.btn_No.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNoCancel:
                    this.btn_Yes.IsDefault = true;
                    this.btn_Yes.Visibility = Visibility.Visible;
                    this.btn_No.Visibility = Visibility.Visible;
                    this.btn_Cancel.Visibility = Visibility.Visible;
                    break;
            }

            // var descriptor = DependencyPropertyDescriptor.FromName("Symbol", this.pi_Icon.GetType(), this.pi_Icon.GetType());

            switch (image) 
            {
                case MessageBoxImage.None:
                    this.pi_Icon.Visibility = Visibility.Collapsed;
                    break;
                case MessageBoxImage.Error:
                    this.pi_Icon.Symbol = Wpf.Ui.Controls.SymbolRegular.ErrorCircle20;
                    // descriptor.SetValue(this.pi_Icon, "ErrorCircle20");
                    this.pi_Icon.Foreground = Brushes.Red;
                    break;
                case MessageBoxImage.Question:
                    this.pi_Icon.Symbol = Wpf.Ui.Controls.SymbolRegular.QuestionCircle20;
                    // descriptor.SetValue(this.pi_Icon, "QuestionCircle20");
                    this.pi_Icon.Foreground = Brushes.Blue;
                    break;
                case MessageBoxImage.Warning:
                    this.pi_Icon.Symbol = Wpf.Ui.Controls.SymbolRegular.Warning20;
                    // descriptor.SetValue(this.pi_Icon, "Warning20");
                    this.pi_Icon.Foreground = Brushes.Orange;
                    break;
                case MessageBoxImage.Information:
                    this.pi_Icon.Symbol = Wpf.Ui.Controls.SymbolRegular.Info20;
                    // descriptor.SetValue(this.pi_Icon, "Info20");
                    this.pi_Icon.Foreground = Brushes.Blue;
                    break;
            }
            // this.MouseDown += (o, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
            // this.Closed += (s, e) => { Result = MessageBoxResult.Cancel; };
        }

        private void btn_OK_Click(object sender, RoutedEventArgs e)
        {
            this.Result = MessageBoxResult.OK;
            // this.Close();
        }

        private void btn_Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Result = MessageBoxResult.Cancel;
            // this.Close();
        }

        private void btn_Yes_Click(object sender, RoutedEventArgs e)
        {
            this.Result = MessageBoxResult.Yes;
            // this.Close();
        }
        private void btn_No_Click(object sender, RoutedEventArgs e)
        {
            this.Result = MessageBoxResult.No;
            // this.Close();
        }

    }
}
