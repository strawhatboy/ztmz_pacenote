using System.Text.RegularExpressions;

namespace ZTMZ.PacenoteTool.Tests.WpfGUI;

public class ScriptEditorPrintLayoutTest
{
    [Fact]
    public void RoadbookPrintStylesUseComfortableMarginsWithoutReducingRowsPerPage()
    {
        var editorPage = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Monaco", "index.html"));

        Assert.Matches(
            new Regex(@"@page\s*\{[^}]*size:\s*A4\s+portrait\s*;[^}]*margin:\s*10mm\s+15mm\s*;", RegexOptions.IgnoreCase),
            editorPage);
        Assert.Matches(
            new Regex(@"#editor-shell\s*\{[^}]*display:\s*block\s*!important[^}]*width:\s*100%\s*!important", RegexOptions.IgnoreCase),
            editorPage);
        Assert.Matches(
            new Regex(@"#roadbook\s*\{[^}]*width:\s*100%\s*!important[^}]*flex:\s*none\s*!important", RegexOptions.IgnoreCase),
            editorPage);
        Assert.Matches(
            new Regex(@"\.roadbook-token-card\s*\{[^}]*min-height:\s*32mm", RegexOptions.IgnoreCase),
            editorPage);
        Assert.Contains("ztmz-roadbook-print-margin-horizontal-mm", editorPage);
        Assert.Contains("ztmz-roadbook-print-margin-vertical-mm", editorPage);
        Assert.Contains("Math.min(30, Math.max(5", editorPage);
        Assert.Contains("millimeters === null", editorPage);
    }
}
