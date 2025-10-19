using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base.UI;

/// <summary>
/// Web server that streams dashboard data to browser clients
/// Serves Lua scripts and resources, clients execute Lua client-side
/// </summary>
public class WebDashboardServer : IDisposable
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    
    private WebApplication? _app;
    private List<WebSocket> _connectedClients = new();
    private readonly object _clientsLock = new();
    
    private List<Dashboard>? _dashboards;
    private string? _dashboardsPath;
    
    public int Port { get; set; } = 8080;
    public bool IsRunning { get; private set; }
    public int ClientCount => _connectedClients.Count;
    
    /// <summary>
    /// Initialize the server with dashboard data
    /// </summary>
    public void Initialize(List<Dashboard> dashboards)
    {
        _dashboards = dashboards;
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
                _app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwrootPath),
                    RequestPath = ""
                });
            }
            else
            {
                _logger.Warn($"wwwroot folder not found at: {wwwrootPath}");
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
            _app.UseWebSockets();
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
            _app.MapGet("/api/dashboards", () =>
            {
                return _dashboards!.Select((d, index) => new
                {
                    id = index,
                    name = d.Descriptor.Name,
                    description = d.Descriptor.Description,
                    author = d.Descriptor.Author,
                    version = d.Descriptor.Version,
                    enabled = d.Descriptor.IsEnabled,
                    previewImage = d.Descriptor.PreviewImagePath != null 
                        ? $"/dashboards/{Path.GetFileName(d.Descriptor.Path)}/{d.Descriptor.PreviewImagePath}"
                        : null
                }).ToList();
            });
            
            // API: Get specific dashboard package
            _app.MapGet("/api/dashboard/{id}", (int id) =>
            {
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
                    imageResources[img.Key] = $"/dashboards/{dashboardFolder}/{img.Value.Path}";
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
                            imageResources[key] = $"/dashboards/{dashboardFolder}/{relativePath}";
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
            
            // Start server in background
            _ = _app.RunAsync();
            
            IsRunning = true;
            _logger.Info($"WebDashboard server started at http://localhost:{Port}");
            _logger.Info($"Access from mobile: http://<PC-IP>:{Port}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start WebDashboard server");
            throw;
        }
    }
    
    /// <summary>
    /// Handle WebSocket connection for real-time data streaming
    /// </summary>
    private async Task HandleWebSocket(HttpContext context)
    {
        var ws = await context.WebSockets.AcceptWebSocketAsync();
        
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
    /// Broadcast game data to all connected clients
    /// Called every frame (60+ times per second)
    /// </summary>
    public async Task BroadcastGameData(GameData data, GameContext context)
    {
        if (_connectedClients.Count == 0) return;
        
        var json = JsonConvert.SerializeObject(new
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
            context = context
        });
        
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);
        
        List<WebSocket> clientsToRemove = new();
        
        lock (_clientsLock)
        {
            foreach (var client in _connectedClients)
            {
                if (client.State == WebSocketState.Open)
                {
                    try
                    {
                        await client.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch
                    {
                        clientsToRemove.Add(client);
                    }
                }
                else
                {
                    clientsToRemove.Add(client);
                }
            }
            
            foreach (var client in clientsToRemove)
            {
                _connectedClients.Remove(client);
            }
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
