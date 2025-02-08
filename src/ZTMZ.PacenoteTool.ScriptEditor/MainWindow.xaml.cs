#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
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
using System.Windows.Threading;
using Microsoft.Win32;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using Wpf.Ui;
using Path = System.IO.Path;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private MonacoController? _monacoController;
        private string _relatedFile;
        private bool _isSaved = true;
        private readonly string PACENOTE_FILTER = "路书文件(*.pacenote) | *.pacenote";
        private readonly string TITLE = "ZTMZ Club 路书脚本编辑工具v1.4";
        private ToolTip _toolTip = new ToolTip();
        private int _intellisenseMode = 0;

        public MainWindowViewModel ViewModel { get; set; }

        public Dispatcher CurrentDispatcher { set; get; }

        public MainWindow()
        {
            ViewModel = new MainWindowViewModel();
            DataContext = this;
            InitializeComponent();
            CurrentDispatcher = Application.Current.Dispatcher;
            this._monacoController = new MonacoController(this.webView);
            ViewModel.SetMonacoController(this._monacoController);
            this.webView.NavigationCompleted += OnWebViewNavigationCompleted;
            webView.UseLayoutRounding = true;
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            webView.Source = new Uri(
                System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, @"Assets\Monaco\index.html")
            );
        }

        private DispatcherOperation<TResult> DispatchAsync<TResult>(Func<TResult> callback)
        {
            return Application.Current.Dispatcher.InvokeAsync(callback);
        }

        private void OnWebViewNavigationCompleted(
        object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e
        )
        {
            DispatchAsync(InitializeEditorAsync);
        }

        private async Task InitializeEditorAsync()
        {
            if (_monacoController == null)
            {
                return;
            }

            await _monacoController.CreateAsync();
            await _monacoController.SetThemeAsync(Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            await _monacoController.SetLanguageAsync(MonacoLanguage.Csharp);
            await _monacoController.SetContentAsync(
                "// This Source Code Form is subject to the terms of the MIT License.\r\n// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.\r\n// Copyright (C) Leszek Pomianowski and WPF UI Contributors.\r\n// All Rights Reserved.\r\n\r\nnamespace Wpf.Ui.Gallery.Models.Monaco;\r\n\r\n[Serializable]\r\npublic record MonacoTheme\r\n{\r\n    public string Base { get; init; }\r\n\r\n    public bool Inherit { get; init; }\r\n\r\n    public IDictionary<string, string> Rules { get; init; }\r\n\r\n    public IDictionary<string, string> Colors { get; init; }\r\n}\r\n"
            );
        }

        private void Btn_save_OnClick(object sender, RoutedEventArgs e)
        {
            this.tryParsePacenote();

            if (string.IsNullOrEmpty(this._relatedFile))
            {
                SaveFileDialog dialog = new SaveFileDialog();
                dialog.Filter = PACENOTE_FILTER;
                if (dialog.ShowDialog() == true)
                {
                    this._relatedFile = dialog.FileName;
                }
                else
                {
                    return;
                }
            }

            this.updateTitle();
        }

        private void Btn_open_OnClick(object sender, RoutedEventArgs e)
        {
            // this.checkFileSaved();

            // open
            // OpenFileDialog dialog = new OpenFileDialog();
            // dialog.Filter = PACENOTE_FILTER;
            // if (dialog.ShowDialog() == true)
            // {
            //     this._relatedFile = dialog.FileName;
            //     this._isSaved = true;
            //     this.updateTitle();
            //     this._monacoController.SetContentAsync(File.ReadAllText(this._relatedFile));
            //     this.tryParsePacenote();
            // }
        }

        private ScriptReader tryParsePacenote()
        {
            // try parse the content
            try
            {
                return null;
            }
            catch (ScriptParseException ex)
            {
                MessageBox.Show(string.Format("语法检查失败：在第{0}行发现未知的短语：{1}", ex.Line + 1, ex.UnexpectedToken), "语法错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }


        public void HandleFileOpen(string file)
        {
            // this.checkFileSaved();


            this._relatedFile = file;
            this._isSaved = true;
            this.updateTitle();
            this.tryParsePacenote();
        }

        private void updateTitle()
        {
            this.Title = this.TITLE + " - " + (!this._isSaved ? "[未保存]" : "") + this._relatedFile;
        }


        private void Btn_importFromCrewChief_OnClick(object sender, RoutedEventArgs e)
        {
            // 覆盖当前文件

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "CrewChief路书文件 (*.json) | *.json";
            if (dialog.ShowDialog() == true)
            {
                ScriptReader reader = ScriptReader.ReadFromJson(dialog.FileName);
                this.updateTitle();
                this.tryParsePacenote();
            }
        }


        private void Btn_adjustDistance_OnClick(object sender, RoutedEventArgs e)
        {
            var reader = this.tryParsePacenote();
            foreach (var record in reader.PacenoteRecords)
            {
                // if (this.txt_adjustDistance.Value.HasValue && record.Distance.HasValue)
                //     record.Distance += this.txt_adjustDistance.Value.Value;
            }

        }


        private void Txt_adjustFontSize_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
        }

        private void Btn_saveAs_OnClick(object sender, RoutedEventArgs e)
        {
            this.tryParsePacenote();

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = PACENOTE_FILTER;
            if (dialog.ShowDialog() == true)
            {
                this._relatedFile = dialog.FileName;
            }
            else
            {
                return;
            }

            this._isSaved = true;
            this.updateTitle();
        }

        private ICommand _saveCommand;

        public ICommand SaveCommand
        {
            get
            {
                if (this._saveCommand == null)
                {
                    this._saveCommand = new ActionCommand(() => { Btn_save_OnClick(null, null); });
                }

                return this._saveCommand;
            }
        }

        private ICommand _saveAsCommand;

        public ICommand SaveAsCommand
        {
            get
            {
                if (this._saveAsCommand == null)
                {
                    this._saveAsCommand = new ActionCommand(() => { Btn_saveAs_OnClick(null, null); });
                }

                return this._saveAsCommand;
            }
        }

        private ICommand _openCommand;

        public ICommand OpenCommand
        {
            get
            {
                if (this._openCommand == null)
                {
                    this._openCommand = new ActionCommand(() => { Btn_open_OnClick(null, null); });
                }

                return this._openCommand;
            }
        }

        private ICommand _importCommand;

        public ICommand ImportCommand
        {
            get
            {
                if (this._importCommand == null)
                {
                    this._importCommand = new ActionCommand(() => { Btn_importFromCrewChief_OnClick(null, null); });
                }

                return this._importCommand;
            }
        }

        private void Txt_adjustDistance_OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Btn_adjustDistance_OnClick(null, null);
            }
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (!this._isSaved)
            {
                var dialogResult = MessageBox.Show("当前文档未保存，点击“是”保存后退出，点击“否”直接退出，点击“取消”继续工作", "未保存的文档！", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                switch (dialogResult)
                {
                    case MessageBoxResult.Yes:
                        Btn_save_OnClick(null, null);
                        break;
                    case MessageBoxResult.No:
                        // do nothing
                        break;
                    case MessageBoxResult.Cancel:
                        e.Cancel = true;
                        break;
                }
            }
        }

        private void cb_intellisenseMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // this._intellisenseMode = this.cb_intellisenseMode.SelectedIndex;
        }

        public void AppendLine(string line)
        {
            CurrentDispatcher.Invoke(() =>
            {
            });
        }


        private void btn_share_Click(object sender, RoutedEventArgs e)
        {
        }

        private void btn_convert_Click(object sender, RoutedEventArgs e)
        {
            // this.AutoConvertTextToAliases(true);
        }

        private void btn_openFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(this._relatedFile))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    string.Format("/select,\"{0}\"", System.IO.Path.GetFullPath(this._relatedFile))));
            }
        }

        private void btn_viewShares_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                "ftp://anonymous@1.116.3.39"));
        }

        private void btn_Wordwrap_Click(object sender, RoutedEventArgs e)
        {
            // if (this.btn_Wordwrap.IsChecked.HasValue)
            // {
            // }
        }

        private void btn_Print_Click(object sender, RoutedEventArgs e)
        {
            // PrintDialog pd = new PrintDialog();
            // if (this.lb_viewMode.SelectedIndex == 0)
            // {
            //     var fd = DocumentPrinter.CreateFlowDocumentForEditor(avalonEditor);
            //     var dRes = pd.ShowDialog();
            //     if (dRes.HasValue && dRes.Value)
            //     {
            //         fd.PageHeight = pd.PrintableAreaHeight;
            //         fd.PageWidth = pd.PrintableAreaWidth;
            //         IDocumentPaginatorSource idocument = fd as IDocumentPaginatorSource;

            //         pd.PrintDocument(idocument.DocumentPaginator, Path.GetFileNameWithoutExtension(this._relatedFile));
            //     }
            // } else
            // {
            //     // print graphical pacenote
            //     var fd = PrintHelper.GetFixedDocument(this.wp_graphical, pd);
            //     var dRes = pd.ShowDialog();
            //     if (dRes.HasValue && dRes.Value)
            //     {
            //         PrintHelper.PrintNoPreview(pd, fd);
            //     }
            // }
        }

        private void lb_viewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // if (this.avalonEditor != null && this.wp_graphical != null)
            // {
            //     if (lb_viewMode.SelectedIndex == 0)
            //     {
            //         this.avalonEditor.Visibility = Visibility.Visible;
            //         this.sv_graphical.Visibility = Visibility.Hidden;
            //     }
            //     else
            //     {
            //         this.avalonEditor.Visibility = Visibility.Hidden;
            //         this.sv_graphical.Visibility = Visibility.Visible;
            //     }
            // }
        }
    }
}
