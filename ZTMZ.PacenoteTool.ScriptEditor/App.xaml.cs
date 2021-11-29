using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace ZTMZ.PacenoteTool.ScriptEditor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static void InitHighlightComponent()
        {
            using var reader = new System.Xml.XmlTextReader("pacenote.xshd");
            var highlighting = ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(reader,
                    ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);

            // insert highlighting rules
            SyntaxAppendRule(highlighting, (from f in ScriptResource.FLAGS select string.Format("@{0}", f.Key)), Colors.DarkBlue);
            SyntaxAppendRule(highlighting, (from f in ScriptResource.ALIAS select string.Format(",{0}", f.Key)), Color.FromRgb(23, 110, 191));
            SyntaxAppendRule(highlighting, (from f in ScriptResource.ALIAS select string.Format("/{0}", f.Key)), Color.FromRgb(143, 88, 232));
            SyntaxAppendRule(highlighting, (from f in ScriptResource.PACENOTES select string.Format(",{0}", f.Key)), Colors.Blue);
            SyntaxAppendRule(highlighting, (from f in ScriptResource.MODIFIERS select string.Format("/{0}", f.Key)), Colors.Purple);


            ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.RegisterHighlighting("pacenote",
                new string[0],
                highlighting);
        }

        private static void SyntaxAppendRule(IHighlightingDefinition def, IEnumerable<string> words, Color color)
        {
            var highlightingRule = new HighlightingRule();
            highlightingRule.Color = new HighlightingColor() { Foreground = new CustomizedBrush(color) };
            var flagsRegexString = string.Join('|', words);
            highlightingRule.Regex = new System.Text.RegularExpressions.Regex(flagsRegexString);
            def.MainRuleSet.Rules.Add(highlightingRule);
        }
        public App()
        {
            InitHighlightComponent();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            MainWindow wnd = new MainWindow();
            if (e.Args.Length == 1)
                wnd.HandleFileOpen(e.Args[0]);
            wnd.Show();
        }
        internal sealed class CustomizedBrush : HighlightingBrush
        {
            private readonly SolidColorBrush brush;
            public CustomizedBrush(Color color)
            {
                brush = CreateFrozenBrush(color);
            }

            public CustomizedBrush(System.Drawing.Color c)
            {
                var c2 = System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);
                brush = CreateFrozenBrush(c2);
            }

            public override Brush GetBrush(ITextRunConstructionContext context)
            {
                return brush;
            }

            public override string ToString()
            {
                return brush.ToString();
            }

            private static SolidColorBrush CreateFrozenBrush(Color color)
            {
                SolidColorBrush brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
    }
}