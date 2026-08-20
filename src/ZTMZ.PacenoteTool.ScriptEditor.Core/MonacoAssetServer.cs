using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace ZTMZ.PacenoteTool.ScriptEditor;

public sealed class MonacoAssetServer : IAsyncDisposable
{
    private readonly string _editorAssetsRoot;
    private readonly string? _iconAssetsRoot;
    private WebApplication? _application;
    private Uri? _baseUri;

    public MonacoAssetServer(string editorAssetsRoot, string? iconAssetsRoot = null)
    {
        _editorAssetsRoot = ValidateDirectory(editorAssetsRoot, nameof(editorAssetsRoot));
        _iconAssetsRoot = string.IsNullOrWhiteSpace(iconAssetsRoot)
            ? null
            : ValidateDirectory(iconAssetsRoot, nameof(iconAssetsRoot));
    }

    public Uri EditorUri => _baseUri == null
        ? throw new InvalidOperationException("The Monaco asset server has not started.")
        : new Uri(_baseUri, "editor/index.html");

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application != null)
        {
            return;
        }

        var indexPath = Path.Combine(_editorAssetsRoot, "index.html");
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("The Monaco editor entry point was not found.", indexPath);
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MonacoAssetServer).Assembly.GetName().Name,
            ContentRootPath = _editorAssetsRoot
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

        var application = builder.Build();
        application.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await next();
        });
        application.UseStaticFiles(CreateStaticFileOptions("/editor", _editorAssetsRoot));
        if (_iconAssetsRoot != null)
        {
            application.UseStaticFiles(CreateStaticFileOptions("/icons", _iconAssetsRoot));
        }

        await application.StartAsync(cancellationToken);
        var address = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses
            .SingleOrDefault();
        if (string.IsNullOrWhiteSpace(address))
        {
            await application.DisposeAsync();
            throw new InvalidOperationException("The Monaco asset server did not publish a listening address.");
        }

        _application = application;
        _baseUri = new Uri(address.EndsWith('/') ? address : address + "/");
    }

    public string ResolveAssetUri(string filePath)
    {
        if (_baseUri == null)
        {
            throw new InvalidOperationException("The Monaco asset server has not started.");
        }

        var fullPath = Path.GetFullPath(filePath);
        if (TryGetRelativePath(_editorAssetsRoot, fullPath, out var editorPath))
        {
            return new Uri(_baseUri, "editor/" + EncodePath(editorPath)).AbsoluteUri;
        }

        if (_iconAssetsRoot != null && TryGetRelativePath(_iconAssetsRoot, fullPath, out var iconPath))
        {
            return new Uri(_baseUri, "icons/" + EncodePath(iconPath)).AbsoluteUri;
        }

        throw new ArgumentOutOfRangeException(
            nameof(filePath),
            filePath,
            "The asset is outside the roots served by the Monaco asset server.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_application == null)
        {
            return;
        }

        var application = _application;
        _application = null;
        _baseUri = null;
        await application.StopAsync();
        await application.DisposeAsync();
    }

    private static StaticFileOptions CreateStaticFileOptions(string requestPath, string root) => new()
    {
        RequestPath = requestPath,
        FileProvider = new PhysicalFileProvider(root),
        ContentTypeProvider = new FileExtensionContentTypeProvider()
    };

    private static string ValidateDirectory(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        return fullPath;
    }

    private static bool TryGetRelativePath(string root, string path, out string relativePath)
    {
        relativePath = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relativePath)
            && !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string EncodePath(string relativePath) => string.Join(
        '/',
        relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
}
