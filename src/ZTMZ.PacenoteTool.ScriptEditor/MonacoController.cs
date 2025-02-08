using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Web.WebView2.Wpf;
using Wpf.Ui.Appearance;

namespace ZTMZ.PacenoteTool.ScriptEditor;
public class MonacoController
{
    private const string EditorContainerSelector = "#root";

    private const string EditorObject = "wpfUiMonacoEditor";

    private volatile WebView2 _webView;

    public MonacoController(WebView2 webView)
    {
        _webView = webView;
    }

    public async Task CreateAsync()
    {
        await _webView.ExecuteScriptAsync(
            $$"""
            const {{EditorObject}} = monaco.editor.create(document.querySelector('{{EditorContainerSelector}}'));
            window.onresize = () => {{{EditorObject}}.layout();}
            """
        );
    }

    public async Task RegisterPacenoteLanguageAsync() {
        await _webView.ExecuteScriptAsync(
            $$$"""
            monaco.languages.register({{
                id: 'pacenote',
                extensions: ['.pacenote'],
                aliases: ['Pacenote', 'pacenote'],
                mimetypes: ['text/pacenote'],
            }});
            """);
    }
 

    public async Task SetThemeAsync(ApplicationTheme appApplicationTheme)
    {
        const string uiThemeName = "wpf-ui-app-theme";
        var baseMonacoTheme = appApplicationTheme == ApplicationTheme.Light ? "vs" : "vs-dark";

        // TODO: Parse theme from object

        await _webView.ExecuteScriptAsync(
            $$$"""
            monaco.editor.defineTheme('{{{uiThemeName}}}', {
                base: '{{{baseMonacoTheme}}}',
                inherit: true,
                rules: [{ background: 'FFFFFF00' }],
                colors: {'editor.background': '#FFFFFF00','minimap.background': '#FFFFFF00',}});
            monaco.editor.setTheme('{{{uiThemeName}}}');
            """
        );
    }

    public async Task SetLanguageAsync(MonacoLanguage monacoLanguage)
    {
        var languageId =
            monacoLanguage == MonacoLanguage.ObjectiveC ? "objective-c" : monacoLanguage.ToString().ToLower();

        await _webView.ExecuteScriptAsync(
            "monaco.editor.setModelLanguage(" + EditorObject + $".getModel(), \"{languageId}\");"
        );
    }

    public async Task SetContentAsync(string contents)
    {
        var literalContents = SymbolDisplay.FormatLiteral(contents, false);
        await _webView.ExecuteScriptAsync($"{EditorObject}.setValue(\"{literalContents}\");");
    }

    string UnescapeLiteral(string literal)
    {
        // Parse the literal string (which includes the surrounding quotes and escape sequences)
        var expression = SyntaxFactory.ParseExpression(literal) as LiteralExpressionSyntax;
        if (expression == null)
            throw new InvalidOperationException("The literal could not be parsed.");

        // Token.ValueText returns the unescaped string content
        return expression.Token.ValueText;
    }

    public async Task<string> GetContentAsync()
    {
        var content = await _webView.ExecuteScriptAsync($"{EditorObject}.getValue();");
        content = content?.ToString() ?? string.Empty;
        // convert from string literal
        return UnescapeLiteral(content);
    }

    public void DispatchScript(string script)
    {
        if (_webView == null)
            return;

        Application.Current.Dispatcher.InvokeAsync(async () => await _webView!.ExecuteScriptAsync(script));
    }
}
