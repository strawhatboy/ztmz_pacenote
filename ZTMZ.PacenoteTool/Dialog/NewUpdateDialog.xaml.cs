﻿using System;
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
        private string _newVersion;
        public NewUpdateDialog(string newVersion, string currentVersion, string changelogUrl)
        {
            _newVersion = newVersion;
            InitializeComponent();
            this.tb_content.Text = string.Format(I18NLoader.Instance["dialog.newUpdate.content"],
                newVersion, currentVersion);
            this.wb_Changelog.Navigate(new Uri(changelogUrl)); 
            this.MouseDown += (o, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
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
