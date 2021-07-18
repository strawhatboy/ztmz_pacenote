using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            using var reader = new System.Xml.XmlTextReader("pacenote.xshd");
            ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.RegisterHighlighting("pacenote",
                new string[0],
                ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(reader,
                    ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance));
        }
    }
}