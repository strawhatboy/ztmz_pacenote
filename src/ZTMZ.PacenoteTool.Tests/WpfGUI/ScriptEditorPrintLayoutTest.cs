using System.Text.RegularExpressions;
using System.Reflection;
using Wpf.Ui.Controls;
using ZTMZ.PacenoteTool.ScriptEditor;
using ZTMZ.PacenoteTool.WpfGUI.Views;

namespace ZTMZ.PacenoteTool.Tests.WpfGUI;

public class ScriptEditorPrintLayoutTest
{
    [Fact]
    public void RoadbookPrintSurfaceIsHostedInsideTheMainWindow()
    {
        Assert.True(typeof(ContentDialog).IsAssignableFrom(typeof(RoadbookPrintWindow)));
        Assert.False(typeof(System.Windows.Window).IsAssignableFrom(typeof(RoadbookPrintWindow)));
    }

    [Fact]
    public void RoadbookPreviewUsesAnExplicitVisibleWidth()
    {
        var dialog = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ZTMZ.PacenoteTool.WpfGUI",
            "Views",
            "RoadbookPrintWindow.xaml"));

        Assert.Contains("x:Name=\"PrintLayoutRoot\" Width=\"1040\" Height=\"600\"", dialog, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"680\" />", dialog, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"360\" />", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void RoadbookPreviewUriKeepsPdfViewerOptionsAsAFragment()
    {
        var createPreviewUri = typeof(RoadbookPrintWindow).GetMethod(
            "CreatePreviewUri",
            BindingFlags.NonPublic | BindingFlags.Static);
        var previewUri = Assert.IsType<Uri>(createPreviewUri?.Invoke(
            null,
            [Path.Combine(Path.GetTempPath(), "roadbook preview.pdf")]));

        Assert.True(previewUri.IsFile);
        Assert.EndsWith("roadbook preview.pdf", previewUri.LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("#toolbar=0&navpanes=0", previewUri.Fragment);
        Assert.DoesNotContain("%23toolbar", previewUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RoadbookPageNumberPosition.BottomLeft, "@bottom-left")]
    [InlineData(RoadbookPageNumberPosition.BottomCenter, "@bottom-center")]
    [InlineData(RoadbookPageNumberPosition.BottomRight, "@bottom-right")]
    public void RoadbookPageNumbersUseTheSelectedMarginBox(
        RoadbookPageNumberPosition position,
        string expectedMarginBox)
    {
        var style = BuildPrintPageStyle(new RoadbookPrintOptions(
            210,
            297,
            false,
            15,
            10,
            ShowPageNumbers: true,
            PageNumberStyle: RoadbookPageNumberStyle.CurrentAndTotal,
            PageNumberPosition: position));

        Assert.Contains(expectedMarginBox, style, StringComparison.Ordinal);
        Assert.Contains("counter(page) ' / ' counter(pages)", style, StringComparison.Ordinal);
    }

    [Fact]
    public void RoadbookPageNumbersCanBeHiddenOrUseOnlyTheCurrentPage()
    {
        var hidden = BuildPrintPageStyle(new RoadbookPrintOptions(
            210, 297, false, 15, 10, ShowPageNumbers: false));
        var currentOnly = BuildPrintPageStyle(new RoadbookPrintOptions(
            210,
            297,
            false,
            15,
            10,
            ShowPageNumbers: true,
            PageNumberStyle: RoadbookPageNumberStyle.Current));

        Assert.Empty(hidden);
        Assert.Contains("content: counter(page);", currentOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("counter(pages)", currentOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void RoadbookPrintStylesUseThePrintableWidthWithoutForcingPaperSettings()
    {
        var editorPage = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Monaco", "index.html"));

        Assert.DoesNotContain("@page", editorPage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setPrintPageStyle(css)", editorPage, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"#editor-shell\s*\{[^}]*display:\s*block\s*!important[^}]*width:\s*100%\s*!important", RegexOptions.IgnoreCase),
            editorPage);
        Assert.Matches(
            new Regex(@"#roadbook\s*\{[^}]*width:\s*100%\s*!important[^}]*flex:\s*none\s*!important", RegexOptions.IgnoreCase),
            editorPage);
        Assert.Matches(
            new Regex(@"\.roadbook-token-card\s*\{[^}]*min-height:\s*32mm", RegexOptions.IgnoreCase),
            editorPage);
        Assert.Matches(
            new Regex(@"\.roadbook-distance-card[^\{]*\{[^}]*background:\s*#f1e3e2\s*!important[^}]*print-color-adjust:\s*exact", RegexOptions.IgnoreCase),
            editorPage);
        Assert.Matches(
            new Regex(@"\.roadbook-block:not\(\.roadbook-distance-card\)\.selected", RegexOptions.IgnoreCase),
            editorPage);
        Assert.DoesNotMatch(
            new Regex(@"\.roadbook-block\.selected\s*,\s*\.roadbook-block\.playing", RegexOptions.IgnoreCase),
            editorPage);
    }

    private static string BuildPrintPageStyle(RoadbookPrintOptions options)
    {
        var method = typeof(MonacoController).GetMethod(
            "BuildPrintPageStyle",
            BindingFlags.NonPublic | BindingFlags.Static);
        return Assert.IsType<string>(method?.Invoke(null, [options]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
