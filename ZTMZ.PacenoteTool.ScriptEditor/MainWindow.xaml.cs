using System;
using System.Collections.Generic;
using System.IO;
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
using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.Win32;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private CompletionWindow _completionWindow;
        private string _relatedFile;
        private bool _isSaved;
        private readonly string PACENOTE_FILTER = "路书文件(*.pacenote) | *.pacenote";
        private readonly string TITLE = "ZTMZ Club 路书脚本编辑工具v1.0.0";

        public MainWindow()
        {
            InitializeComponent();
            this.avalonEditor.TextArea.TextEntering += (sender, args) =>
            {
                if (args.Text.Length > 0 && _completionWindow != null)
                {
                    if (!char.IsLetterOrDigit(args.Text[0]) && args.Text[0] != '_')
                    {
                        // Whenever a non-letter is typed while the completion window is open,
                        // insert the currently selected element.
                        _completionWindow.CompletionList.RequestInsertion(args);
                    }
                }
            };
            this.avalonEditor.TextArea.TextEntered += (sender, args) =>
            {
                this._isSaved = false;
                if (args.Text == ",")
                {
                    // Open code completion after the user has pressed dot:
                    _completionWindow = new CompletionWindow(avalonEditor.TextArea);
                    IList<ICompletionData> data = _completionWindow.CompletionList.CompletionData;
                    foreach (var r in ScriptResource.PACENOTES)
                    {
                        data.Add(new ScriptCompletionData(r.Key, r.Value));
                    }

                    _completionWindow.Show();
                    _completionWindow.Closed += delegate { _completionWindow = null; };
                }
                else if (args.Text == "/")
                {
                    // Open code completion after the user has pressed dot:
                    _completionWindow = new CompletionWindow(avalonEditor.TextArea);
                    IList<ICompletionData> data = _completionWindow.CompletionList.CompletionData;
                    foreach (var r in ScriptResource.MODIFIERS)
                    {
                        data.Add(new ScriptCompletionData(r.Key, r.Value));
                    }

                    _completionWindow.Show();
                    _completionWindow.Closed += delegate { _completionWindow = null; };
                }
            };
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
                    this.Title = TITLE + " - " + this._relatedFile;
                }
                else
                {
                    return;
                }
            }

            File.WriteAllText(this._relatedFile, avalonEditor.Text);
            this._isSaved = true;
        }

        private void Btn_open_OnClick(object sender, RoutedEventArgs e)
        {
            if (!this._isSaved && !string.IsNullOrEmpty(this._relatedFile))
            {
                var result = MessageBox.Show("当前文件尚未保存，是否先保存当前文件？", "保存文件", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    Btn_save_OnClick(null, null);
                    return;
                }
            }
            
            // open
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = PACENOTE_FILTER;
            if (dialog.ShowDialog() == true)
            {
                avalonEditor.Text = File.ReadAllText(dialog.FileName);
                this.Title = this.TITLE + " - " + dialog.FileName;
                this.tryParsePacenote();
            }
        }

        private void tryParsePacenote()
        {
            
            // try parse the content
            try
            {
                ScriptReader.ReadFromString(avalonEditor.Text);
            }
            catch (ScriptParseException ex)
            {
                avalonEditor.ScrollToLine(ex.Line);
                MessageBox.Show(string.Format("语法检查失败：在第{0}行发现未知的短语：{1}", ex.Line + 1, ex.UnexpectedToken));
            }
        }
    }
}