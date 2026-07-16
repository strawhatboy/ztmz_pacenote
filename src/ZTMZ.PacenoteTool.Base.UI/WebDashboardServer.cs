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
    
    private List<Dashboard>? _dashboards;
    private string? _dashboardsPath;
    private Func<Dashboard, DashboardOutputTarget> _dashboardOutputTargetResolver =
        dashboard => dashboard.Descriptor.IsEnabled
            ? DashboardOutputTarget.Both
            : DashboardOutputTarget.None;
    
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
            }
            _logger.Info($"Client {clientId} disconnected. Total clients: {_connectedClients.Count}");
        }
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
            var commandsJson = JsonConvert.SerializeObject(
                new
                {
                    type = "drawCommands",
                    commands = commands
                },
                _webDashboardJsonSettings);
            
            var commandsBytes = Encoding.UTF8.GetBytes(commandsJson);
            var commandsSegment = new ArraySegment<byte>(commandsBytes);
            
            await SendToAllClients(commandsSegment);
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
        
        await SendToAllClients(segment);
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
