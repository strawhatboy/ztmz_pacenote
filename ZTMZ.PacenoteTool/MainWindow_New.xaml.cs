using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
using NAudio.Wave;
using OnlyR.Core.Models;
using OnlyR.Core.Recorder;
using ZTMZ.PacenoteTool.Base;
using System.Globalization;
using System.IO;
using MaterialDesignThemes.Wpf;
using System.Threading;
using ZTMZ.PacenoteTool.Dialog;
using Constants = ZTMZ.PacenoteTool.Base.Constants;

namespace ZTMZ.PacenoteTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow_New : Window
    {

        public MainWindow_New()
        {
            InitializeComponent();
        }

        public ComboBox CB_Codrivers => this.cb_codrivers;
    }

}
