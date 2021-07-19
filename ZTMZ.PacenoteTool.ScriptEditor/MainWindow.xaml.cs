#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Path = System.IO.Path;

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
        private ToolTip _toolTip = new ToolTip();

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
            this.txt_adjustFontSize.ValueChanged += this.Txt_adjustFontSize_OnValueChanged;
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

            File.WriteAllText(this._relatedFile, avalonEditor.Text);
            this._isSaved = true;
            this.updateTitle();
        }

        private void Btn_open_OnClick(object sender, RoutedEventArgs e)
        {
            this.checkFileSaved();

            // open
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = PACENOTE_FILTER;
            if (dialog.ShowDialog() == true)
            {
                avalonEditor.Text = File.ReadAllText(dialog.FileName);
                this._relatedFile = dialog.FileName;
                this._isSaved = true;
                this.updateTitle();
                this.tryParsePacenote();
            }
        }

        private ScriptReader tryParsePacenote()
        {
            // try parse the content
            try
            {
                return ScriptReader.ReadFromString(avalonEditor.Text);
            }
            catch (ScriptParseException ex)
            {
                avalonEditor.ScrollToLine(ex.Line);
                MessageBox.Show(string.Format("语法检查失败：在第{0}行发现未知的短语：{1}", ex.Line + 1, ex.UnexpectedToken));
                return null;
            }
        }

        private void AvalonEditor_OnMouseHover(object sender, MouseEventArgs e)
        {
            var pos = avalonEditor.GetPositionFromPoint(e.GetPosition(avalonEditor));
            if (pos != null)
            {
                try
                {
                    this._toolTip.PlacementTarget = this; // required for property inheritance
                    var offset = this.avalonEditor.Document.GetOffset(pos.Value.Line, pos.Value.Column);
                    // find token near the mouse
                    // search forward
                    var end = offset;
                    var keySeparator = new char[] {'\n', '\r', ',', '/', '#', ' ', '\t'};
                    while (end < this.avalonEditor.Text.Length &&
                           !keySeparator.Contains(this.avalonEditor.Text[end]))
                    {
                        end++;
                    }

                    var start = offset;
                    Debug.WriteLine("start pos: " + start.ToString());
                    while (start >= 0 && !keySeparator.Contains(this.avalonEditor.Text[start]))
                    {
                        start--;
                    }

                    start++;

                    var content = this.avalonEditor.Document.GetText(start, end - start);
                    content = content.Trim();
                    if (ScriptResource.PACENOTES.ContainsKey(content))
                    {
                        content = "路书" + content + ": " + ScriptResource.PACENOTES[content];
                    }
                    else if (ScriptResource.MODIFIERS.ContainsKey(content))
                    {
                        content = "路书修饰：" + content + ": " + ScriptResource.MODIFIERS[content];
                    }

                    this._toolTip.Content = content;
                    this._toolTip.IsOpen = true;
                    e.Handled = true;
                }
                catch
                {
                    // 暴力
                }
            }
        }

        private void AvalonEditor_OnMouseHoverStopped(object sender, MouseEventArgs e)
        {
            this._toolTip.IsOpen = false;
        }

        private void AvalonEditor_OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                string[] files = (string[]) e.Data.GetData(DataFormats.FileDrop);

                // Assuming you have one file that you care about, pass it off to whatever
                // handling code you have defined.
                handleFileOpen(files[0]);
            }
        }

        private void handleFileOpen(string file)
        {
            this.checkFileSaved();

            avalonEditor.Text = File.ReadAllText(file);
            this._relatedFile = file;
            this._isSaved = true;
            this.updateTitle();
            this.tryParsePacenote();
        }

        private void updateTitle()
        {
            this.Title = this.TITLE + " - " + (!this._isSaved ? "[未保存]" : "") + this._relatedFile;
        }

        private void checkFileSaved()
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
        }

        private void Btn_importFromCrewChief_OnClick(object sender, RoutedEventArgs e)
        {
            // 覆盖当前文件
            
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "CrewChief路书文件 (*.json) | *.json";
            if (dialog.ShowDialog() == true)
            {
                ScriptReader reader = ScriptReader.ReadFromJson(dialog.FileName);
                avalonEditor.Text = reader.ToString();
                this.updateTitle();
                this.tryParsePacenote();
            }
        }

        private void AvalonEditor_OnTextChanged(object? sender, EventArgs e)
        {
            this._isSaved = false;
            this.updateTitle();
        }

        private void Btn_adjustDistance_OnClick(object sender, RoutedEventArgs e)
        {
            var reader = this.tryParsePacenote();
            foreach (var record in reader.PacenoteRecords)
            {
                if (this.txt_adjustDistance.Value.HasValue)
                    record.Distance += this.txt_adjustDistance.Value.Value;
            }

            this.avalonEditor.Text = reader.ToString();
        }


        private void Txt_adjustFontSize_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (this.txt_adjustFontSize.Value.HasValue)
                this.avalonEditor.FontSize = this.txt_adjustFontSize.Value.Value;
        }
    }
}