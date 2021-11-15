﻿#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Search;
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
        private bool _isSaved = true;
        private readonly string PACENOTE_FILTER = "路书文件(*.pacenote) | *.pacenote";
        private readonly string TITLE = "ZTMZ Club 路书脚本编辑工具v1.2.0";
        private ToolTip _toolTip = new ToolTip();
        private int _intellisenseMode = 0;

        public MainWindow()
        {
            InitializeComponent();
            SearchPanel.Install(this.avalonEditor);
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
                    _completionWindow = ScriptCompletionData.GetCompletionWindow(avalonEditor, ScriptResource.TYPE_PACENOTE, this._intellisenseMode);
                    _completionWindow.Show();
                    _completionWindow.Closed += delegate { _completionWindow = null; };
                }
                else if (args.Text == "/")
                {
                    // Open code completion after the user has pressed dot:
                    _completionWindow = ScriptCompletionData.GetCompletionWindow(avalonEditor, ScriptResource.TYPE_MODIFIER, this._intellisenseMode);

                    _completionWindow.Show();
                    _completionWindow.Closed += delegate { _completionWindow = null; };
                }
                else if (args.Text == "@")
                {
                    // Open code completion after the user has pressed dot:
                    _completionWindow = ScriptCompletionData.GetCompletionWindow(avalonEditor, ScriptResource.TYPE_FLAG, this._intellisenseMode);

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
                MessageBox.Show(string.Format("语法检查失败：在第{0}行发现未知的短语：{1}", ex.Line + 1, ex.UnexpectedToken), "语法错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    var keySeparator = new char[] {'\n', '\r', ',', '/', '#', ' ', '\t', '@'};
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

                    content = ScriptResource.GetTokenDescription(content);

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
                HandleFileOpen(files[0]);
            }
        }

        public void HandleFileOpen(string file)
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
                var result = MessageBox.Show("当前文件尚未保存，是否先保存当前文件？", "保存文件", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                if (this.txt_adjustDistance.Value.HasValue && record.Distance.HasValue)
                    record.Distance += this.txt_adjustDistance.Value.Value;
            }

            this.avalonEditor.Text = reader.ToString();
        }


        private void Txt_adjustFontSize_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (this.txt_adjustFontSize.Value.HasValue)
                this.avalonEditor.FontSize = this.txt_adjustFontSize.Value.Value;
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

            File.WriteAllText(this._relatedFile, avalonEditor.Text);
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
            this._intellisenseMode = this.cb_intellisenseMode.SelectedIndex;
        }

        public void AppendLine(string line)
        {
            this.avalonEditor.Text += line + System.Environment.NewLine;
        }

    }
}