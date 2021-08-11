using System;
using System.Collections.Generic;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    public class ScriptCompletionData : ICompletionData
    {
        public string Type { get; private set; }
        public ScriptCompletionData(string type, string text, string description)
        {
            this.Type = type;
            this.Text = text;
            this.Description = description;
            
        }

        public System.Windows.Media.ImageSource Image => null;

        public string Text { get; private set; }

        // Use this property if you want to show a fancy UIElement in the list.
        public object Content => this.Text;

        public object Description
        {
            get;
        }

        public double Priority => 1;

        public void Complete(TextArea textArea, ISegment completionSegment,
            EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, this.Text);
        }
        
        public static CompletionWindow GetCompletionWindow(TextEditor textEditor, string type, int mode)
        {
            var _completionWindow = new CompletionWindow(textEditor.TextArea);
            IList<ICompletionData> data = _completionWindow.CompletionList.CompletionData;

            if (mode == 0 || mode == 1)
            {
                foreach (var alias in ScriptResource.ALIAS_CONSTRUCTED)
                {
                    data.Add(new ScriptCompletionData(ScriptResource.TYPE_ALIAS, alias.Key, ScriptResource.GetTokenDescription(alias.Key)));
                }
            } 

            if (mode == 1 || mode == 2)
            {
                if (type == ScriptResource.TYPE_PACENOTE)
                {
                    foreach (var obj in ScriptResource.PACENOTES)
                    {
                        data.Add(new ScriptCompletionData(ScriptResource.TYPE_ALIAS, obj.Key, ScriptResource.GetTokenDescription(obj.Key)));
                    }
                } else if (type == ScriptResource.TYPE_MODIFIER)
                {
                    foreach (var obj in ScriptResource.MODIFIERS)
                    {
                        data.Add(new ScriptCompletionData(ScriptResource.TYPE_ALIAS, obj.Key, ScriptResource.GetTokenDescription(obj.Key)));
                    }
                } else if (type == ScriptResource.TYPE_FLAG)
                {
                    foreach (var obj in ScriptResource.FLAGS)
                    {
                        data.Add(new ScriptCompletionData(ScriptResource.TYPE_FLAG, obj.Key, ScriptResource.GetTokenDescription(obj.Key)));
                    }
                }
            }

            return _completionWindow;
        }
    }
}