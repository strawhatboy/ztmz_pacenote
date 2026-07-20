using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base.UI;

/// <summary>
/// Web server that streams server-rendered dashboard commands and resources to browser clients
/// </summary>
public class WebDashboardServer : IDisposable
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerSettings _webDashboardJsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };
    
    private WebApplication? _app;
    private List<WebSocket> _connectedClients = new();
    private readonly object _clientsLock = new();
    private readonly Dictionary<WebSocket, long> _layeredSceneRevisionByClient = new();
    private readonly Dictionary<WebSocket, bool> _browserLuaSupportedByClient = new();
    private readonly Dictionary<WebSocket, long> _browserLuaSceneRevisionByClient = new();
    private readonly object _layeredFrameLock = new();
    private readonly object _browserLuaSceneLock = new();
    private readonly Dictionary<string, IReadOnlyList<DrawingCommand>> _staticLayerCommands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _staticLayerSignatures = new(StringComparer.Ordinal);
    private LayeredScene? _layeredScene;
    private long _nextLayeredSceneRevision;
    private BrowserLuaScene _browserLuaScene;
    private long _nextBrowserLuaSceneRevision;
    
    private List<Dashboard>? _dashboards;
    private string? _dashboardsPath;
    private Func<Dashboard, DashboardOutputTarget> _dashboardOutputTargetResolver =
        dashboard => dashboard.Descriptor.IsEnabled
            ? DashboardOutputTarget.Both
            : DashboardOutputTarget.None;

    private sealed class LayeredScene
    {
        public LayeredScene(long revision, string layoutSignature, IReadOnlyList<WebDashboardDrawLayer> layers)
        {
            Revision = revision;
            LayoutSignature = layoutSignature;
            Layers = layers;
        }

        public long Revision { get; }

        public string LayoutSignature { get; }

        public IReadOnlyList<WebDashboardDrawLayer> Layers { get; }
    }

    private sealed class LayeredSendResult
    {
        public LayeredSendResult(WebSocket client, bool wasSnapshot, WebSocket failedClient)
        {
            Client = client;
            WasSnapshot = wasSnapshot;
            FailedClient = failedClient;
        }

        public WebSocket Client { get; }

        public bool WasSnapshot { get; }

        public WebSocket FailedClient { get; }
    }

    private sealed class BrowserLuaScene
    {
        public BrowserLuaScene(long revision, string signature, WebDashboardBrowserLuaScene scene)
        {
            Revision = revision;
            Signature = signature;
            Scene = scene;
        }

        public long Revision { get; }

        public string Signature { get; }

        public WebDashboardBrowserLuaScene Scene { get; }
    }

    private sealed class BrowserLuaSendResult
    {
        public BrowserLuaSendResult(WebSocket client, bool wasSnapshot, WebSocket failedClient)
        {
            Client = client;
            WasSnapshot = wasSnapshot;
            FailedClient = failedClient;
        }

        public WebSocket Client { get; }

        public bool WasSnapshot { get; }

        public WebSocket FailedClient { get; }
    }

    private sealed class BrowserLuaClientControlMessage
    {
        public string Type { get; set; } = string.Empty;

        public bool? Supported { get; set; }

        public string Reason { get; set; } = string.Empty;
    }
    
    public int Port { get; set; } = 8080;
    public bool IsRunning { get; private set; }
    public int ClientCount
    {
        get
        {
            lock (_clientsLock)
            {
                return _connectedClients.Count;
            }
        }
    }

    /// <summary>
    /// Browser-side Lua is used only when every connected client has explicitly
    /// advertised support. This keeps an older client on the established command
    /// stream instead of leaving it with an empty dashboard.
    /// </summary>
    public bool CanExecuteBrowserLuaDashboards
    {
        get
        {
            lock (_clientsLock)
            {
                return _connectedClients.Count > 0
                    && _connectedClients.All(client => _browserLuaSupportedByClient.TryGetValue(client, out var supported)
                        && supported);
            }
        }
    }

    public static string GetDashboardUrl(int port)
    {
        return GetDashboardUrls(port).FirstOrDefault() ?? string.Empty;
    }

    public static string GetDashboardImageCacheKey(int dashboardId, string imageKey)
    {
        return $"{dashboardId}:{imageKey}";
    }

    public static string GetDashboardImageUrl(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        try
        {
            var dashboardsPath = Path.GetFullPath(
                AppLevelVariables.Instance.GetPath(Constants.PATH_DASHBOARDS));
            var relativePath = Path.GetRelativePath(dashboardsPath, Path.GetFullPath(imagePath));
            if (Path.IsPathRooted(relativePath)
                || relativePath.Equals("..", StringComparison.Ordinal)
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return null;
            }

            var encodedPath = string.Join(
                "/",
                relativePath.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
            return $"/dashboards/{encodedPath}";
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<string> GetDashboardUrls(int port)
    {
        if (port is < 1 or > 65535)
        {
            return Array.Empty<string>();
        }

        var urls = new List<string>();
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(networkInterface =>
                    networkInterface.OperationalStatus == OperationalStatus.Up
                    && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(networkInterface => networkInterface.GetIPProperties().GatewayAddresses.Any(
                    gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.Any.Equals(gateway.Address)))
                .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
                .Select(unicastAddress => unicastAddress.Address)
                .Where(IsUsableIpv4Address)
                .Distinct()
                .ToList();

            urls.AddRange(addresses.Select(address => $"http://{address}:{port}"));
        }
        catch
        {
            // localhost remains a usable address when network discovery is unavailable.
        }

        urls.Add($"http://localhost:{port}");
        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsUsableIpv4Address(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] != 169 || bytes[1] != 254;
    }
    
    /// <summary>
    /// Initialize the server with dashboard data
    /// </summary>
    public void Initialize(
        List<Dashboard> dashboards,
        Func<Dashboard, DashboardOutputTarget> dashboardOutputTargetResolver = null)
    {
        _dashboards = dashboards;
        _dashboardOutputTargetResolver = dashboardOutputTargetResolver ??
            (dashboard => dashboard.Descriptor.IsEnabled
                ? DashboardOutputTarget.Both
                : DashboardOutputTarget.None);
        _dashboardsPath = AppLevelVariables.Instance.GetPath(Constants.PATH_DASHBOARDS);
        _logger.Info($"WebDashboard server initialized with {dashboards.Count} dashboards");
    }
    
    /// <summary>
    /// Start the web server
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            _logger.Warn("WebDashboard server already running");
            return;
        }
        
        if (_dashboards == null || _dashboardsPath == null)
        {
            throw new InvalidOperationException("Server not initialized. Call Initialize() first.");
        }
        
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://*:{Port}");
            
            // Suppress default ASP.NET logging in console
            builder.Logging.ClearProviders();
            
            _app = builder.Build();
            
            // Serve wwwroot static files (HTML, JS, CSS)
            var wwwrootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            if (Directory.Exists(wwwrootPath))
            {
                // Enable default files (index.html) support
                _app.UseDefaultFiles(new DefaultFilesOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwrootPath),
                    RequestPath = ""
                });
                
                _app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwrootPath),
                    RequestPath = "",
                    OnPrepareResponse = DisableWebDashboardClientCaching
                });
            }
            else
            {
                _logger.Warn($"wwwroot folder not found at: {wwwrootPath}");
            }

            var fontsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts");
            if (Directory.Exists(fontsPath))
            {
                var fontContentTypes = new FileExtensionContentTypeProvider();
                fontContentTypes.Mappings[".ttf"] = "font/ttf";
                _app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(fontsPath),
                    RequestPath = "/fonts",
                    ContentTypeProvider = fontContentTypes,
                    OnPrepareResponse = DisableWebDashboardClientCaching
                });
            }
            else
            {
                _logger.Warn($"Dashboard fonts folder not found at: {fontsPath}");
            }
            
            // Serve dashboard resources as static files
            if (Directory.Exists(_dashboardsPath))
            {
                _app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(_dashboardsPath),
                    RequestPath = "/dashboards",
                    ServeUnknownFileTypes = true,
                    DefaultContentType = "application/octet-stream"
                });
            }
            
            // WebSocket endpoint for real-time game data
            _app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            });
            _app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        await HandleWebSocket(context);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else
                {
                    await next();
                }
            });
            
            // API: List available dashboards
            _app.MapGet("/api/dashboards", (HttpResponse response) =>
            {
                DisableWebDashboardClientCaching(response);
                return _dashboards!.Select((d, index) =>
                {
                    var outputTarget = ResolveOutputTarget(d);
                    return new
                    {
                        id = index,
                        name = d.Descriptor.Name,
                        description = d.Descriptor.Description,
                        author = d.Descriptor.Author,
                        version = d.Descriptor.Version,
                        enabled = outputTarget != DashboardOutputTarget.None,
                        overlayEnabled = outputTarget.Includes(DashboardOutputTarget.Overlay),
                        webEnabled = outputTarget.Includes(DashboardOutputTarget.Web),
                        outputTarget = GetOutputTargetName(outputTarget),
                        supportsWebDashboardClientLua = d.Descriptor.SupportsWebDashboardClientLua,
                        previewImage = d.Descriptor.PreviewImagePath != null
                            ? $"/dashboards/{Path.GetFileName(d.Descriptor.Path)}/{d.Descriptor.PreviewImagePath}"
                            : null
                    };
                }).ToList();
            });
            
            // API: Get specific dashboard package
            _app.MapGet("/api/dashboard/{id}", (HttpResponse response, int id) =>
            {
                DisableWebDashboardClientCaching(response);
                if (id < 0 || id >= _dashboards!.Count)
                {
                    return Results.NotFound();
                }
                
                var dashboard = _dashboards[id];
                var dashboardFolder = Path.GetFileName(dashboard.Descriptor.Path);
                
                // Read Lua script
                var scriptPath = Path.Combine(dashboard.Descriptor.Path, Constants.FILE_LUA_SCRIPT);
                var luaScript = File.Exists(scriptPath) ? File.ReadAllText(scriptPath) : "";
                
                // Read common Lua scripts
                var commonPath = Path.Combine(_dashboardsPath!, "common");
                var commonScripts = new List<string>();
                if (Directory.Exists(commonPath))
                {
                    foreach (var luaFile in Directory.GetFiles(commonPath, "*.lua"))
                    {
                        commonScripts.Add(File.ReadAllText(luaFile));
                    }
                }
                
                // Build image resource map
                var imageResources = new Dictionary<string, string>();
                
                // Single images
                foreach (var img in dashboard.Descriptor.ImageResources)
                {
                    imageResources[GetDashboardImageCacheKey(id, img.Key)] =
                        $"/dashboards/{dashboardFolder}/{img.Value.Path}";
                }
                
                // Image directories
                foreach (var imgDir in dashboard.Descriptor.ImageResourcesInDirectory)
                {
                    if (imgDir.Value == null) continue;

                    var dirPath = Path.Combine(dashboard.Descriptor.Path, imgDir.Value.Path);
                    if (!Directory.Exists(dirPath)) continue;

                    foreach (var file in Directory.GetFiles(dirPath))
                    {
                        var ext = Path.GetExtension(file).ToLower();
                        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif")
                        {
                            var fileName = Path.GetFileNameWithoutExtension(file);
                            var key = $"{imgDir.Key}@{fileName}";
                            var relativePath = Path.GetRelativePath(dashboard.Descriptor.Path, file).Replace('\\', '/');
                            imageResources[GetDashboardImageCacheKey(id, key)] =
                                $"/dashboards/{dashboardFolder}/{relativePath}";
                        }
                    }
                }
                
                var package = new
                {
                    id = id,
                    name = dashboard.Descriptor.Name,
                    description = dashboard.Descriptor.Description,
                    author = dashboard.Descriptor.Author,
                    version = dashboard.Descriptor.Version,
                    supportsWebDashboardClientLua = dashboard.Descriptor.SupportsWebDashboardClientLua,
                    luaScript = luaScript,
                    commonScripts = commonScripts,
                    imageResources = imageResources,
                    settings = dashboard.DashboardConfigurations.PropertyValue
                        .Select((val, idx) => new
                        {
                            key = dashboard.DashboardConfigurations.PropertyName.Keys.ElementAt(idx),
                            value = val
                        })
                        .ToDictionary(x => x.key, x => x.value)
                };
                
                return Results.Json(package);
            });
            
            // StartAsync completes only after Kestrel has bound the configured port.
            _app.StartAsync().GetAwaiter().GetResult();
            IsRunning = true;
            _logger.Info("WebDashboard server started at {0}", GetDashboardUrl(Port));
        }
        catch (Exception ex)
        {
            IsRunning = false;
            try
            {
                _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // Preserve the startup exception as the actionable failure.
            }
            _app = null;
            _logger.Error(ex, "Failed to start WebDashboard server");
            throw;
        }
    }

    private DashboardOutputTarget ResolveOutputTarget(Dashboard dashboard)
    {
        var outputTarget = _dashboardOutputTargetResolver(dashboard);
        return outputTarget.IsValid()
            ? outputTarget
            : dashboard.Descriptor.IsEnabled
                ? DashboardOutputTarget.Both
                : DashboardOutputTarget.None;
    }

    private static string GetOutputTargetName(DashboardOutputTarget outputTarget)
    {
        return outputTarget switch
        {
            DashboardOutputTarget.Overlay => "overlay",
            DashboardOutputTarget.Web => "web",
            DashboardOutputTarget.Both => "both",
            _ => "off"
        };
    }

    private static void DisableWebDashboardClientCaching(StaticFileResponseContext context)
    {
        DisableWebDashboardClientCaching(context.Context.Response);
    }

    private static void DisableWebDashboardClientCaching(HttpResponse response)
    {
        // The dashboard client changes with the desktop application, so always
        // fetch its static assets and dynamic dashboard metadata on an ordinary refresh.
        response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0, must-revalidate";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["Expires"] = "0";
    }
    
    /// <summary>
    /// Handle WebSocket connection for real-time data streaming
    /// </summary>
    private async Task HandleWebSocket(HttpContext context)
    {
        var ws = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
        {
            // Dashboard frames are repetitive telemetry commands and contain no
            // authentication secrets, so per-message compression is appropriate.
            DangerousEnableCompression = true
        });
        
        lock (_clientsLock)
        {
            _connectedClients.Add(ws);
            _layeredSceneRevisionByClient[ws] = 0;
            _browserLuaSupportedByClient[ws] = false;
            _browserLuaSceneRevisionByClient[ws] = 0;
        }
        
        var clientId = ws.GetHashCode();
        _logger.Info($"Client {clientId} connected. Total clients: {_connectedClients.Count}");
        
        try
        {
            // Keep connection alive - client will receive data via BroadcastGameData
            var buffer = new byte[1024];
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text
                    && result.EndOfMessage
                    && result.Count > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    HandleClientControlMessage(ws, message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"WebSocket error for client {clientId}");
        }
        finally
        {
            lock (_clientsLock)
            {
                _connectedClients.Remove(ws);
                _layeredSceneRevisionByClient.Remove(ws);
                _browserLuaSupportedByClient.Remove(ws);
                _browserLuaSceneRevisionByClient.Remove(ws);
            }
            _logger.Info(
                $"Client {clientId} disconnected. Total clients: {_connectedClients.Count}. "
                + $"State: {ws.State}; Close status: {ws.CloseStatus}; Description: {ws.CloseStatusDescription}");
        }
    }

    private void HandleClientControlMessage(WebSocket client, string message)
    {
        BrowserLuaClientControlMessage controlMessage;
        try
        {
            controlMessage = JsonConvert.DeserializeObject<BrowserLuaClientControlMessage>(message);
        }
        catch (JsonException)
        {
            return;
        }

        if (controlMessage == null)
        {
            return;
        }

        lock (_clientsLock)
        {
            switch (controlMessage.Type)
            {
                case "requestSceneSnapshot":
                    if (_layeredSceneRevisionByClient.ContainsKey(client))
                    {
                        _layeredSceneRevisionByClient[client] = 0;
                    }
                    break;
                case "requestBrowserLuaScene":
                    if (_browserLuaSceneRevisionByClient.ContainsKey(client))
                    {
                        _browserLuaSceneRevisionByClient[client] = 0;
                    }
                    break;
                case "browserLuaCapabilities":
                    if (_browserLuaSupportedByClient.ContainsKey(client))
                    {
                        _browserLuaSupportedByClient[client] = controlMessage.Supported == true;
                        _browserLuaSceneRevisionByClient[client] = 0;
                        _logger.Info(
                            $"Browser Lua capability from client {client.GetHashCode()}: {controlMessage.Supported == true}");
                    }
                    break;
                case "browserLuaFallback":
                    if (_browserLuaSupportedByClient.ContainsKey(client))
                    {
                        _browserLuaSupportedByClient[client] = false;
                        _browserLuaSceneRevisionByClient[client] = 0;
                        _logger.Warn(
                            $"Browser Lua client {client.GetHashCode()} requested compatibility fallback: "
                            + FormatBrowserLuaFallbackReason(controlMessage.Reason));
                    }
                    break;
            }
        }
    }

    private static string FormatBrowserLuaFallbackReason(string reason)
    {
        var normalized = (reason ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "No reason supplied.";
        }

        return normalized.Length <= 256 ? normalized : normalized[..256];
    }
    
    /// <summary>
    /// Broadcast game data and drawing commands to all connected clients
    /// Called every frame (60+ times per second)
    /// </summary>
    public async Task BroadcastGameData(GameData data, GameContext context, List<DrawingCommand> commands = null)
    {
        if (ClientCount == 0) return;
        
        // If we have drawing commands, send them instead of just game data
        if (commands != null && commands.Count > 0)
        {
            InvalidateLayeredClientRevisions();
            InvalidateBrowserLuaClientRevisions();
            await BroadcastCommandListAsync(commands);
            return;
        }
        
        // Fallback: send game data only (legacy support)
        var json = JsonConvert.SerializeObject(
            new
            {
                type = "gameData",
                data = new
                {
                    // Telemetry data
                    Time = data.Time,
                    LapTime = data.LapTime,
                    LapDistance = data.LapDistance,
                    CompletionRate = data.CompletionRate,
                    Speed = data.Speed,
                    TrackLength = data.TrackLength,

                    // Wheel speeds
                    SpeedRearLeft = data.SpeedRearLeft,
                    SpeedRearRight = data.SpeedRearRight,
                    SpeedFrontLeft = data.SpeedFrontLeft,
                    SpeedFrontRight = data.SpeedFrontRight,

                    // Controls
                    Clutch = data.Clutch,
                    Brake = data.Brake,
                    Throttle = data.Throttle,
                    HandBrake = data.HandBrake,
                    HandBrakeValid = data.HandBrakeValid,
                    Steering = data.Steering,

                    // Engine
                    Gear = data.Gear,
                    MaxGears = data.MaxGears,
                    RPM = data.RPM,
                    MaxRPM = data.MaxRPM,
                    IdleRPM = data.IdleRPM,
                    ShiftLightsFraction = data.ShiftLightsFraction,
                    ShiftLightsRPMStart = data.ShiftLightsRPMStart,
                    ShiftLightsRPMEnd = data.ShiftLightsRPMEnd,
                    ShiftLightsRPMValid = data.ShiftLightsRPMValid,

                    // Forces
                    G_long = data.G_long,
                    G_lat = data.G_lat,

                    // Temperatures
                    BrakeTempRearLeft = data.BrakeTempRearLeft,
                    BrakeTempRearRight = data.BrakeTempRearRight,
                    BrakeTempFrontLeft = data.BrakeTempFrontLeft,
                    BrakeTempFrontRight = data.BrakeTempFrontRight,

                    // Suspension
                    SuspensionRearLeft = data.SuspensionRearLeft,
                    SuspensionRearRight = data.SuspensionRearRight,
                    SuspensionFrontLeft = data.SuspensionFrontLeft,
                    SuspensionFrontRight = data.SuspensionFrontRight,
                    SuspensionSpeedRearLeft = data.SuspensionSpeedRearLeft,
                    SuspensionSpeedRearRight = data.SuspensionSpeedRearRight,
                    SuspensionSpeedFrontLeft = data.SuspensionSpeedFrontLeft,
                    SuspensionSpeedFrontRight = data.SuspensionSpeedFrontRight,

                    // Position
                    CarPos = data.CarPos,
                    PosX = data.PosX,
                    PosY = data.PosY,
                    PosZ = data.PosZ
                },
                context = new
                {
                    // Only serialize data properties, not delegates/functions
                    TrackName = context.TrackName,
                    AudioPackage = context.AudioPackage,
                    ScriptAuthor = context.ScriptAuthor,
                    PacenoteType = context.PacenoteType,
                    CarName = context.CarName,
                    CarClass = context.CarClass,
                    LocalReplayValid = context.LocalReplayValid,
                    LastGameState = context.LastGameState,
                    GameState = context.GameState,
                    GameStateChangedParameters = context.GameStateChangedParameters
                }
            },
            _webDashboardJsonSettings);
        
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        InvalidateBrowserLuaClientRevisions();
        await SendToAllClients(segment);
    }

    /// <summary>
    /// Broadcasts a captured command frame. Dashboards with explicit static-layer
    /// markers use retained canvases; every other dashboard keeps the established
    /// complete-frame protocol. Browser-Lua dashboard IDs identify canvases which
    /// are rendered locally alongside the retained command layers.
    /// </summary>
    public async Task BroadcastDrawFrameAsync(
        WebDashboardDrawFrame frame,
        IReadOnlyCollection<int> browserLuaDashboardIds = null)
    {
        if (frame == null || ClientCount == 0)
        {
            return;
        }

        var normalizedBrowserLuaDashboardIds = browserLuaDashboardIds?
            .Where(id => id >= 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray()
            ?? Array.Empty<int>();
        var isHybridFrame = normalizedBrowserLuaDashboardIds.Length > 0;

        // A browser-Lua client stops its local runtime when compatibility commands
        // arrive. Make its next browser-Lua frame a complete scene snapshot.
        if (!isHybridFrame)
        {
            InvalidateBrowserLuaClientRevisions();
        }

        if (!CanUseLayeredRendering(frame))
        {
            InvalidateBrowserLuaClientRevisions();
            InvalidateLayeredClientRevisions();
            await BroadcastCommandListAsync(frame.LegacyCommands);
            return;
        }

        var scene = BuildLayeredScene(frame);
        var snapshotSegment = SerializeMessage(new
        {
            type = "layeredDrawCommands",
            sceneRevision = scene.Revision,
            browserLuaDashboardIds = normalizedBrowserLuaDashboardIds,
            layers = scene.Layers.Select(CreateLayerPayload).ToList()
        });
        var deltaSegment = SerializeMessage(new
        {
            type = "layeredDrawCommands",
            sceneRevision = scene.Revision,
            browserLuaDashboardIds = normalizedBrowserLuaDashboardIds,
            dynamicLayers = scene.Layers
                .Where(layer => layer.Kind == WebDashboardDrawLayerKind.Dynamic)
                .Select(CreateLayerPayload)
                .ToList()
        });

        await SendLayeredFrameToAllClients(scene.Revision, snapshotSegment, deltaSegment);
    }

    /// <summary>
    /// Sends a compact telemetry frame for dashboards whose Lua script is executed
    /// by the browser. The dashboard package and settings are included only when a
    /// client needs a new scene snapshot.
    /// </summary>
    public async Task BroadcastBrowserLuaTelemetryAsync(
        WebDashboardBrowserLuaScene requestedScene,
        GameData data,
        GameContext context,
        bool isHybrid = false)
    {
        if (requestedScene == null || ClientCount == 0)
        {
            return;
        }

        var scene = BuildBrowserLuaScene(requestedScene);
        var telemetry = CreateBrowserLuaTelemetry(data);
        var telemetryContext = CreateBrowserLuaTelemetryContext(data, context);
        var snapshotTelemetryContext = CreateBrowserLuaTelemetryContext(
            data,
            context,
            includeReplayTimeline: true);
        var snapshotSegment = SerializeMessage(new
        {
            type = "browserLuaTelemetry",
            sceneRevision = scene.Revision,
            hybrid = isHybrid,
            dashboards = scene.Scene.Dashboards.Select(CreateBrowserLuaDashboardPayload).ToList(),
            config = scene.Scene.Config,
            data = telemetry,
            context = snapshotTelemetryContext
        });
        var deltaSegment = SerializeMessage(new
        {
            type = "browserLuaTelemetry",
            sceneRevision = scene.Revision,
            hybrid = isHybrid,
            data = telemetry,
            context = telemetryContext
        });

        await SendBrowserLuaTelemetryToAllClients(scene.Revision, snapshotSegment, deltaSegment);
    }

    private BrowserLuaScene BuildBrowserLuaScene(WebDashboardBrowserLuaScene requestedScene)
    {
        var signature = JsonConvert.SerializeObject(
            new
            {
                requestedScene.Config,
                dashboards = requestedScene.Dashboards
                    .OrderBy(dashboard => dashboard.Order)
                    .ThenBy(dashboard => dashboard.Id)
                    .Select(CreateBrowserLuaDashboardPayload)
                    .ToList()
            },
            _webDashboardJsonSettings);
        lock (_browserLuaSceneLock)
        {
            if (_browserLuaScene == null
                || !string.Equals(_browserLuaScene.Signature, signature, StringComparison.Ordinal))
            {
                _browserLuaScene = new BrowserLuaScene(
                    ++_nextBrowserLuaSceneRevision,
                    signature,
                    requestedScene);
            }

            return _browserLuaScene;
        }
    }

    private static object CreateBrowserLuaTelemetry(GameData data)
    {
        return new
        {
            Brake = FiniteTelemetryValue(data.Brake),
            Clutch = FiniteTelemetryValue(data.Clutch),
            CompletionRate = FiniteTelemetryValue(data.CompletionRate),
            Gear = FiniteTelemetryValue(data.Gear),
            G_lat = FiniteTelemetryValue(data.G_lat),
            G_long = FiniteTelemetryValue(data.G_long),
            HandBrake = FiniteTelemetryValue(data.HandBrake),
            data.HandBrakeValid,
            LapDistance = FiniteTelemetryValue(data.LapDistance),
            LapTime = FiniteTelemetryValue(data.LapTime),
            MaxGears = FiniteTelemetryValue(data.MaxGears),
            MaxRPM = FiniteTelemetryValue(data.MaxRPM),
            RPM = FiniteTelemetryValue(data.RPM),
            ShiftLightsRPMEnd = FiniteTelemetryValue(data.ShiftLightsRPMEnd),
            ShiftLightsRPMStart = FiniteTelemetryValue(data.ShiftLightsRPMStart),
            data.ShiftLightsRPMValid,
            Speed = FiniteTelemetryValue(data.Speed),
            Steering = FiniteTelemetryValue(data.Steering),
            Throttle = FiniteTelemetryValue(data.Throttle),
            TrackLength = FiniteTelemetryValue(data.TrackLength),
            Time = FiniteTelemetryValue(data.Time),
            GameDataText = data.ToString(),
            GameSpecificDataText = data.GameSpecificDataToString()
        };
    }

    private static float FiniteTelemetryValue(float value)
    {
        return float.IsFinite(value) ? value : 0.0f;
    }

    private static object CreateBrowserLuaDashboardPayload(WebDashboardBrowserLuaDashboard dashboard)
    {
        var orderedSettings = dashboard.Settings
            .OrderBy(setting => setting.Key, StringComparer.Ordinal)
            .ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal);
        return new
        {
            id = dashboard.Id,
            order = dashboard.Order,
            configurationRevision = dashboard.ConfigurationRevision,
            settings = orderedSettings
        };
    }

    private static object CreateBrowserLuaTelemetryContext(
        GameData data,
        GameContext context,
        bool includeReplayTimeline = false)
    {
        var localReplayValid = context?.LocalReplayValid == true;
        var comparison = context?.LocalReplayComparison ?? ReplayComparisonSnapshot.Unavailable;
        var comparisonValid = localReplayValid && comparison.Valid;
        var localReplayDelta = 0.0f;
        if (comparisonValid)
        {
            localReplayDelta = comparison.DeltaAtCurrentProgress;
        }
        else if (localReplayValid)
        {
            try
            {
                localReplayDelta = context.GetDeltaByDistanceAndTime(data.LapDistance, data.LapTime);
            }
            catch
            {
                localReplayDelta = 0.0f;
            }
        }

        if (!float.IsFinite(localReplayDelta))
        {
            localReplayDelta = 0.0f;
        }

        var comparisonPayload = new
        {
            Valid = comparisonValid,
            CurrentCompletionRate = FiniteTelemetryValue(comparison.CurrentCompletionRate),
            BestTimeAtCurrentProgress = FiniteTelemetryValue(comparison.BestTimeAtCurrentProgress),
            DeltaAtCurrentProgress = comparisonValid
                ? FiniteTelemetryValue(comparison.DeltaAtCurrentProgress)
                : 0.0f,
            BestCompletionRateAtCurrentTime = FiniteTelemetryValue(comparison.BestCompletionRateAtCurrentTime),
            BestDistanceAtCurrentTime = FiniteTelemetryValue(comparison.BestDistanceAtCurrentTime)
        };

        if (!includeReplayTimeline)
        {
            return new
            {
                TrackName = context?.TrackName,
                AudioPackage = context?.AudioPackage,
                ScriptAuthor = context?.ScriptAuthor,
                PacenoteType = context?.PacenoteType,
                CarName = context?.CarName,
                CarClass = context?.CarClass,
                LocalReplayValid = localReplayValid,
                LocalReplayFinishTime = FiniteTelemetryValue(context?.LocalReplayFinishTime ?? 0.0f),
                LocalReplay = new
                {
                    checkpoints = context?.LocalReplay?.checkpoints ?? 0,
                    trackLength = FiniteTelemetryValue(context?.LocalReplay?.track_length ?? 0.0f)
                },
                LocalReplayComparison = comparisonPayload,
                GameState = context?.GameState ?? 0,
                LocalReplayDelta = localReplayDelta
            };
        }

        return new
        {
            TrackName = context?.TrackName,
            AudioPackage = context?.AudioPackage,
            ScriptAuthor = context?.ScriptAuthor,
            PacenoteType = context?.PacenoteType,
            CarName = context?.CarName,
            CarClass = context?.CarClass,
            LocalReplayValid = localReplayValid,
            LocalReplayFinishTime = FiniteTelemetryValue(context?.LocalReplayFinishTime ?? 0.0f),
            LocalReplay = new
            {
                checkpoints = context?.LocalReplay?.checkpoints ?? 0,
                trackLength = FiniteTelemetryValue(context?.LocalReplay?.track_length ?? 0.0f)
            },
            LocalReplayComparison = comparisonPayload,
            GameState = context?.GameState ?? 0,
            LocalReplayDelta = localReplayDelta,
            BestReplayTimeline = CreateBrowserLuaReplayTimeline(context, localReplayValid)
        };
    }

    private static IReadOnlyList<object> CreateBrowserLuaReplayTimeline(
        GameContext context,
        bool localReplayValid)
    {
        if (!localReplayValid || context?.LocalReplayDetailsPerTimes == null)
        {
            return Array.Empty<object>();
        }

        const int maximumSampleCount = 512;
        var referenceTimeline = ReplayTimelineLookup.From(
            context.LocalReplayDetailsPerTimes,
            ReplayComparisonCalculator.CreateTerminalSample(context.LocalReplay));
        var orderedSamples = referenceTimeline.OrderedByTime
            .Where(sample => float.IsFinite(sample.time) && float.IsFinite(sample.distance))
            .ToList();
        if (orderedSamples.Count == 0)
        {
            return Array.Empty<object>();
        }

        if (orderedSamples.Count <= maximumSampleCount)
        {
            return orderedSamples
                .Select(sample => (object)new { Time = sample.time, Distance = sample.distance })
                .ToList();
        }

        var sampledTimeline = new List<object>(maximumSampleCount);
        for (var index = 0; index < maximumSampleCount; index++)
        {
            var sourceIndex = (int)Math.Round(
                index * (orderedSamples.Count - 1) / (double)(maximumSampleCount - 1));
            var sample = orderedSamples[sourceIndex];
            sampledTimeline.Add(new { Time = sample.time, Distance = sample.distance });
        }

        return sampledTimeline;
    }

    private async Task SendBrowserLuaTelemetryToAllClients(
        long sceneRevision,
        ArraySegment<byte> snapshotSegment,
        ArraySegment<byte> deltaSegment)
    {
        List<(WebSocket Client, bool NeedsSnapshot)> clientsToSend;
        lock (_clientsLock)
        {
            clientsToSend = _connectedClients
                .Where(client => _browserLuaSupportedByClient.TryGetValue(client, out var supported) && supported)
                .Select(client => (
                    Client: client,
                    NeedsSnapshot: !_browserLuaSceneRevisionByClient.TryGetValue(client, out var clientRevision)
                        || clientRevision != sceneRevision))
                .ToList();
        }

        var sendTasks = clientsToSend
            .Select(async client => new BrowserLuaSendResult(
                client.Client,
                client.NeedsSnapshot,
                await SendToClientAsync(
                    client.Client,
                    client.NeedsSnapshot ? snapshotSegment : deltaSegment)))
            .ToArray();
        var results = await Task.WhenAll(sendTasks);

        lock (_clientsLock)
        {
            foreach (var result in results)
            {
                if (result.FailedClient != null)
                {
                    _connectedClients.Remove(result.FailedClient);
                    _layeredSceneRevisionByClient.Remove(result.FailedClient);
                    _browserLuaSupportedByClient.Remove(result.FailedClient);
                    _browserLuaSceneRevisionByClient.Remove(result.FailedClient);
                    continue;
                }

                if (result.WasSnapshot && _browserLuaSceneRevisionByClient.ContainsKey(result.Client))
                {
                    _browserLuaSceneRevisionByClient[result.Client] = sceneRevision;
                }
            }
        }
    }

    private async Task BroadcastCommandListAsync(IReadOnlyList<DrawingCommand> commands)
    {
        if (commands == null || commands.Count == 0)
        {
            return;
        }

        var commandsSegment = SerializeMessage(new
        {
            type = "drawCommands",
            commands = commands
        });
        await SendToAllClients(commandsSegment);
    }

    private static object CreateLayerPayload(WebDashboardDrawLayer layer)
    {
        return new
        {
            id = layer.Id,
            kind = layer.Kind == WebDashboardDrawLayerKind.Static ? "static" : "dynamic",
            commands = layer.Commands
        };
    }

    private static ArraySegment<byte> SerializeMessage(object message)
    {
        var json = JsonConvert.SerializeObject(message, _webDashboardJsonSettings);
        return new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
    }

    public static bool CanUseLayeredRendering(WebDashboardDrawFrame frame)
    {
        if (!frame.CanUseLayeredRendering)
        {
            return false;
        }

        foreach (var layer in frame.Layers)
        {
            if (layer.Kind == WebDashboardDrawLayerKind.Dynamic
                && layer.Commands.Any(command => command is ClearCommand))
            {
                // A clear in a dynamic layer historically clears the whole canvas,
                // which cannot be represented safely by an isolated retained layer.
                return false;
            }

            if (layer.Kind == WebDashboardDrawLayerKind.Static
                && !IsSelfContainedStaticLayer(layer))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSelfContainedStaticLayer(WebDashboardDrawLayer layer)
    {
        var savedStateDepth = 0;
        foreach (var command in layer.Commands)
        {
            switch (command)
            {
                case RotateCommand:
                case RotateAtCommand:
                case TranslateCommand:
                case ScaleCommand:
                case ResetTransformCommand:
                    // A retained canvas cannot safely pass transforms to the dynamic
                    // layer, so this stays on the complete-frame compatibility path.
                    return false;
                case SaveCommand:
                case SetClipCommand:
                    savedStateDepth++;
                    break;
                case RestoreCommand:
                case ReleaseClipCommand:
                    savedStateDepth--;
                    if (savedStateDepth < 0)
                    {
                        return false;
                    }
                    break;
            }
        }

        return savedStateDepth == 0;
    }

    private LayeredScene BuildLayeredScene(WebDashboardDrawFrame frame)
    {
        lock (_layeredFrameLock)
        {
            var layoutSignature = string.Join(
                "|",
                frame.Layers.Select(layer => $"{layer.Id}:{(int)layer.Kind}"));
            var layoutChanged = _layeredScene == null
                || !string.Equals(_layeredScene.LayoutSignature, layoutSignature, StringComparison.Ordinal);
            var staticLayerIds = frame.Layers
                .Where(layer => layer.Kind == WebDashboardDrawLayerKind.Static)
                .Select(layer => layer.Id)
                .ToHashSet(StringComparer.Ordinal);
            var staticChanged = layoutChanged;

            foreach (var layer in frame.Layers.Where(layer => layer.Kind == WebDashboardDrawLayerKind.Static))
            {
                if (!layer.StaticContentProvided && _staticLayerCommands.ContainsKey(layer.Id))
                {
                    continue;
                }

                var signature = JsonConvert.SerializeObject(layer.Commands, _webDashboardJsonSettings);
                if (!_staticLayerSignatures.TryGetValue(layer.Id, out var currentSignature)
                    || !string.Equals(currentSignature, signature, StringComparison.Ordinal))
                {
                    _staticLayerSignatures[layer.Id] = signature;
                    _staticLayerCommands[layer.Id] = layer.Commands.ToList();
                    staticChanged = true;
                }
            }

            if (layoutChanged)
            {
                foreach (var staleLayerId in _staticLayerCommands.Keys
                    .Where(layerId => !staticLayerIds.Contains(layerId))
                    .ToList())
                {
                    _staticLayerCommands.Remove(staleLayerId);
                    _staticLayerSignatures.Remove(staleLayerId);
                }
            }

            var sceneLayers = frame.Layers
                .Select(layer => layer.Kind == WebDashboardDrawLayerKind.Static
                    ? new WebDashboardDrawLayer(
                        layer.Id,
                        layer.Kind,
                        _staticLayerCommands.TryGetValue(layer.Id, out var commands)
                            ? commands
                            : Array.Empty<DrawingCommand>())
                    : new WebDashboardDrawLayer(layer.Id, layer.Kind, layer.Commands))
                .ToList();
            var revision = staticChanged || _layeredScene == null
                ? ++_nextLayeredSceneRevision
                : _layeredScene.Revision;
            _layeredScene = new LayeredScene(revision, layoutSignature, sceneLayers);
            return _layeredScene;
        }
    }

    private async Task SendLayeredFrameToAllClients(
        long sceneRevision,
        ArraySegment<byte> snapshotSegment,
        ArraySegment<byte> deltaSegment)
    {
        List<(WebSocket Client, bool NeedsSnapshot)> clientsToSend;
        lock (_clientsLock)
        {
            clientsToSend = _connectedClients
                .Select(client => (
                    Client: client,
                    NeedsSnapshot: !_layeredSceneRevisionByClient.TryGetValue(client, out var clientRevision)
                        || clientRevision != sceneRevision))
                .ToList();
        }

        var sendTasks = clientsToSend
            .Select(async client => new LayeredSendResult(
                client.Client,
                client.NeedsSnapshot,
                await SendToClientAsync(
                    client.Client,
                    client.NeedsSnapshot ? snapshotSegment : deltaSegment)))
            .ToArray();
        var results = await Task.WhenAll(sendTasks);

        lock (_clientsLock)
        {
            foreach (var result in results)
            {
                if (result.FailedClient != null)
                {
                    _connectedClients.Remove(result.FailedClient);
                    _layeredSceneRevisionByClient.Remove(result.FailedClient);
                    _browserLuaSupportedByClient.Remove(result.FailedClient);
                    _browserLuaSceneRevisionByClient.Remove(result.FailedClient);
                    continue;
                }

                if (result.WasSnapshot && _layeredSceneRevisionByClient.ContainsKey(result.Client))
                {
                    _layeredSceneRevisionByClient[result.Client] = sceneRevision;
                }
            }
        }
    }

    private void InvalidateLayeredClientRevisions()
    {
        lock (_clientsLock)
        {
            foreach (var client in _connectedClients)
            {
                _layeredSceneRevisionByClient[client] = 0;
            }
        }
    }

    private void InvalidateBrowserLuaClientRevisions()
    {
        lock (_clientsLock)
        {
            foreach (var client in _connectedClients)
            {
                _browserLuaSceneRevisionByClient[client] = 0;
            }
        }
    }

    /// <summary>
    /// Send data to all connected WebSocket clients
    /// </summary>
    private async Task SendToAllClients(ArraySegment<byte> segment)
    {
        List<WebSocket> clientsToSend;
        
        // Copy clients list inside lock
        lock (_clientsLock)
        {
            clientsToSend = new List<WebSocket>(_connectedClients);
        }
        
        // Independent clients send in parallel so a slow device does not hold
        // up every other dashboard viewer.
        var sendTasks = clientsToSend
            .Select(client => SendToClientAsync(client, segment))
            .ToArray();
        var clientsToRemove = (await Task.WhenAll(sendTasks))
            .Where(client => client != null)
            .ToList();
        
        // Remove failed clients inside lock
        if (clientsToRemove.Count > 0)
        {
            lock (_clientsLock)
            {
                foreach (var client in clientsToRemove)
                {
                    _connectedClients.Remove(client);
                    _layeredSceneRevisionByClient.Remove(client);
                    _browserLuaSupportedByClient.Remove(client);
                    _browserLuaSceneRevisionByClient.Remove(client);
                }
            }
        }
    }

    private static async Task<WebSocket> SendToClientAsync(
        WebSocket client,
        ArraySegment<byte> segment)
    {
        if (client.State != WebSocketState.Open)
        {
            return client;
        }

        try
        {
            await client.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            return null;
        }
        catch
        {
            return client;
        }
    }
    
    /// <summary>
    /// Stop the web server
    /// </summary>
    public void Stop()
    {
        if (!IsRunning) return;
        
        try
        {
            _app?.StopAsync().Wait(TimeSpan.FromSeconds(5));
            _app?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            
            lock (_clientsLock)
            {
                _connectedClients.Clear();
                _layeredSceneRevisionByClient.Clear();
                _browserLuaSupportedByClient.Clear();
                _browserLuaSceneRevisionByClient.Clear();
            }

            lock (_layeredFrameLock)
            {
                _staticLayerCommands.Clear();
                _staticLayerSignatures.Clear();
                _layeredScene = null;
                _nextLayeredSceneRevision = 0;
            }

            lock (_browserLuaSceneLock)
            {
                _browserLuaScene = null;
                _nextBrowserLuaSceneRevision = 0;
            }

            IsRunning = false;
            _logger.Info("WebDashboard server stopped");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error stopping WebDashboard server");
        }
    }
    
    public void Dispose()
    {
        Stop();
    }
}
