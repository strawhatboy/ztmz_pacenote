using System;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    public class ScriptCompletionData : ICompletionData
    {
        public ScriptCompletionData(string text, string description)
        {
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
        
        
    }
}