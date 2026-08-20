using Xunit;
using ZTMZ.PacenoteTool.ScriptEditor;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class MonacoEditorControllerTest
{
    [Fact]
    public async Task ControllerUsesBrowserNeutralHostProtocol()
    {
        var bridge = new FakeMonacoBridge { NextResult = "\"100\"" };
        using var controller = new MonacoEditorController(bridge);
        var catalog = new PacenoteLanguageCatalog
        {
            Tokens =
            [
                new PacenoteLanguageToken
                {
                    Id = 1,
                    PrimaryFilename = "6_left",
                    Description = "Left six"
                }
            ]
        };

        await controller.CreateAsync();
        await controller.ConfigurePacenoteLanguageAsync(catalog);

        Assert.Contains(
            bridge.Scripts,
            script => script.Contains("window.ztmzEditor.create", StringComparison.Ordinal));
        Assert.Contains(
            bridge.Scripts,
            script => script.Contains("window.ztmzHost.receive", StringComparison.Ordinal)
                && script.Contains("6_left", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ControllerNormalizesWebViewScriptStringResults()
    {
        var bridge = new FakeMonacoBridge { NextResult = "\"line 1\\nline 2\"" };
        using var controller = new MonacoEditorController(bridge);

        Assert.Equal("line 1\nline 2", await controller.GetContentAsync());
    }

    [Fact]
    public void ControllerRaisesChangedOnlyForEditorMessages()
    {
        var bridge = new FakeMonacoBridge();
        using var controller = new MonacoEditorController(bridge);
        var changed = 0;
        controller.ContentChanged += (_, _) => changed++;

        bridge.RaiseMessage("{\"type\":\"ignored\"}");
        bridge.RaiseMessage("not-json");
        bridge.RaiseMessage("{\"type\":\"changed\"}");

        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task AssetServerServesOnlyConfiguredRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ztmz-monaco-{Guid.NewGuid():N}");
        var editorRoot = Path.Combine(root, "editor");
        var iconRoot = Path.Combine(root, "icons");
        Directory.CreateDirectory(editorRoot);
        Directory.CreateDirectory(iconRoot);
        await File.WriteAllTextAsync(Path.Combine(editorRoot, "index.html"), "editor");
        await File.WriteAllTextAsync(Path.Combine(iconRoot, "test.svg"), "icon");

        try
        {
            await using var server = new MonacoAssetServer(editorRoot, iconRoot);
            await server.StartAsync();
            using var client = new HttpClient();

            Assert.Equal("editor", await client.GetStringAsync(server.EditorUri));
            Assert.Equal(
                "icon",
                await client.GetStringAsync(server.ResolveAssetUri(Path.Combine(iconRoot, "test.svg"))));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                server.ResolveAssetUri(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class FakeMonacoBridge : IMonacoWebViewBridge
    {
        public event EventHandler<MonacoWebMessageReceivedEventArgs>? WebMessageReceived;

        public List<string> Scripts { get; } = [];

        public string? NextResult { get; init; }

        public Task<string?> InvokeScriptAsync(string script)
        {
            Scripts.Add(script);
            return Task.FromResult(NextResult);
        }

        public void RaiseMessage(string body) => WebMessageReceived?.Invoke(
            this,
            new MonacoWebMessageReceivedEventArgs(body));
    }
}
