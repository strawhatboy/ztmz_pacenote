using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;
using GameOverlay.Drawing;
using Neo.IronLua;
using Newtonsoft.Json;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base.UI;


public class GameContext {
    public string TrackName { set; get; } = "";
    public string AudioPackage { set; get; } = "";
    public string ScriptAuthor { set; get; } = "";
    public string PacenoteType { set; get; } = "";
    public string CarName { set; get; } = "";
    public string CarClass { set; get; } = "";
    public bool LocalReplayValid { set; get; } = false;
    public List<ReplayDetailsPerCheckpoint> LocalReplayDetailsPerCheckpoints { set; get; } = new();
    public List<ReplayDetailsPerTime> LocalReplayDetailsPerTimes { set; get; } = new();
    public Dictionary<float, float> LocalReplayDetailsPerTimesDict { set; get; } = new();
    public Replay LocalReplay { set; get; } = new();
    public float LocalReplayFinishTime { set; get; }
    /// <summary>
    /// One immutable, core-calculated comparison for the current telemetry packet.
    /// Dashboards should prefer this over replay lookup delegates for live values.
    /// </summary>
    public ReplayComparisonSnapshot LocalReplayComparison { set; get; } = ReplayComparisonSnapshot.Unavailable;

    /// <summary>
    /// Get the delta time between the current time and the given distance
    /// </summary>
    public Func<float, float> GetDelta { set; get; } = (float distance) => 0;
    public Func<float, float, float> GetDeltaByDistanceAndTime { set; get; } = (float distance, float time) => 0;
    public Func<int, float> GetDeltaByCheckpoint { set; get; } = (int checkpoint) => 0;
    public Func<float, float> GetTimeByDistance { set; get; } = (float distance) => 0;
    public Func<float, float> GetBestTimeByDistance { set; get; } = (float distance) => 0;
    public Func<float, float> GetBestDistanceByTime { set; get; } = (float time) => 0;
    public int LastGameState { set; get; }
    public int GameState { set; get; }
    public Dictionary<string, object> GameStateChangedParameters { set; get; } = new();
}

public class DashboardScriptArguments {
    // Changed from Graphics to object to support both real Graphics and GraphicsCommandProxy
    public object Graphics { get; set; }
    public Config Config { get; set; }
    public I18NLoader I18NLoader { get; set; }
    public GameData GameData { get; set; }
    public GameContext GameContext { get; set; }

    public Dashboard Self { get; set; }

    public GameOverlayDrawingHelper GameOverlayDrawingHelper { get; set; }

    /// <summary>
    /// Lets shared Lua dashboards opt into web-only rendering behavior without
    /// changing their native overlay path.
    /// </summary>
    public bool IsWebDashboard { get; set; }

    /// <summary>
    /// Static WebDashboard artwork must be emitted for this render tick. The
    /// server reuses the previous static canvas layer otherwise.
    /// </summary>
    public bool WebDashboardStaticLayerDirty { get; set; }

    /// <summary>
    /// A native dashboard can skip its static artwork when the overlay has a valid
    /// cached bitmap for it.
    /// </summary>
    public bool NativeStaticLayerEnabled { get; set; }

    /// <summary>
    /// Requests only the dashboard's native static artwork. Built-in dashboards
    /// use NativeStaticLayerGraphics for this isolated rendering pass.
    /// </summary>
    public bool NativeStaticLayerRenderOnly { get; set; }

    /// <summary>
    /// An offscreen graphics surface used only while rebuilding a native static
    /// image layer. It is null during normal dashboard rendering.
    /// </summary>
    public object NativeStaticLayerGraphics { get; set; }
}

public class DashboardConfigs : CommonGameConfigs {

}

/// <summary>
/// Dashboard 
///     json descriptor
///         - name
///         - description
///         - author
///         - version
///         - preview image path ? maybe can be rendered by WPF
///         - lua script path
///         - image resources (path of image numbers font, images, etc.)
///     lua script
///         - onInit
///         - onUpdate
///         - onExit
///     
/// </summary>
public class Dashboard {
    
    private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private long _configurationRevision;

    public DashboardDescriptor Descriptor { get; set; }

    // reuse CommonGameConfig as dashboard configuration :) settings.json
    public DashboardConfigs DashboardConfigurations { set; get; }

    public Dashboard(DashboardDescriptor descriptor) {
        Descriptor = descriptor;
        loadConfig();
    }

    public Dashboard(string jsonDescriptorPath) {
        Descriptor = JsonConvert.DeserializeObject<DashboardDescriptor>(File.ReadAllText(jsonDescriptorPath));
        Descriptor.Path = Path.GetDirectoryName(jsonDescriptorPath);
        loadConfig();
        loadPreviewImage();
    }

    private void loadConfig() { 
        var dashBoardConfig = JsonConvert.DeserializeObject<DashboardConfigs>(File.ReadAllText(Path.Combine(Descriptor.Path, Constants.FILE_SETTINGS)));
        if (File.Exists(Path.Combine(Descriptor.Path, Constants.FILE_USER_SETTINGS))) {
            var userConfig = JsonConvert.DeserializeObject<DashboardConfigs>(File.ReadAllText(Path.Combine(Descriptor.Path, Constants.FILE_USER_SETTINGS)));
            dashBoardConfig.Merge(userConfig);
        } else {
            // create user settings file
            File.WriteAllText(Path.Combine(Descriptor.Path, Constants.FILE_USER_SETTINGS), JsonConvert.SerializeObject(dashBoardConfig, Formatting.Indented));
        }
        DashboardConfigurations = dashBoardConfig;
        Descriptor.IsEnabled = (bool)dashBoardConfig["dashboards.settings.enabled"];
    }

    private void loadPreviewImage() {
        if (!string.IsNullOrEmpty(Descriptor.PreviewImagePath)) {
            this.PreviewImage = new BitmapImage(new Uri(Path.Combine(Descriptor.Path, Descriptor.PreviewImagePath)));
        } else {
            this.PreviewImage = new BitmapImage(new Uri("pack://application:,,,/ZTMZ.PacenoteTool.Base.UI;component/unknown_dashboard_preview_image.png"));
        }
        this.PreviewImage.Freeze();
    }

    public void SetIsEnable(bool value) {
        Descriptor.IsEnabled = value;
        DashboardConfigurations["dashboards.settings.enabled"] = value;
        Interlocked.Increment(ref _configurationRevision);
    }

    public long ConfigurationRevision => Interlocked.Read(ref _configurationRevision);

    public BitmapImage PreviewImage { get; set; }   // use wpf compatible image
    public Dictionary<string, object> ImageResources { get; set; }
    public Dictionary<string, DashboardImageResourceSource> ImageResourceSources { get; private set; }

    private LuaGlobal LuaG { get; set; }

    private Lua _lua;

    public bool IsLoaded => LuaG != null;

    private void loadImageResources(DashboardScriptArguments args) {
        // load resources

        this.ImageResources = new Dictionary<string, object>();
        this.ImageResourceSources = new Dictionary<string, DashboardImageResourceSource>();
        foreach (var imageResource in Descriptor.ImageResources) {
            var resourcePath = GetResourcePath(imageResource.Value.Path);
            this.ImageResources.Add(
                imageResource.Key,
                imageResource.Value.GetImage(args.Graphics, imageResource.Key, resourcePath));
            this.ImageResourceSources.Add(
                imageResource.Key,
                new DashboardImageResourceSource(resourcePath, imageResource.Value.FormatGUID));
        }

        foreach (var imageResourceDirectory in Descriptor.ImageResourcesInDirectory) {
            // imageResourceDirectory.Value is the ImageDescriptor contains the path of images and formatGUID
            if (imageResourceDirectory.Value == null) continue;

            var directory = GetResourcePath(imageResourceDirectory.Value.Path);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.GetFiles(directory, "*.*").Where(
                s => s.ToLower().EndsWith(".png") || s.ToLower().EndsWith(".jpg") || s.ToLower().EndsWith(".jpeg") || s.ToLower().EndsWith(".bmp") || s.ToLower().EndsWith(".gif") 
            )) {
                // Warning, this will overwrite the same key if the file name is the same
                // so append the directory name to the key, separated by @
                var imageDescriptor = new DashboardResourceImageDescriptor() {
                    Path = file,
                    FormatGUID = imageResourceDirectory.Value.FormatGUID
                };
                var resourceKey = imageResourceDirectory.Key + "@" + Path.GetFileNameWithoutExtension(file);
                this.ImageResources.Add(resourceKey, imageDescriptor.GetImage(args.Graphics, resourceKey, file));
                this.ImageResourceSources.Add(
                    resourceKey,
                    new DashboardImageResourceSource(file, imageDescriptor.FormatGUID));
            }
        }
    }

    private string GetResourcePath(string resourcePath) {
        return Path.IsPathRooted(resourcePath)
            ? resourcePath
            : Path.Combine(Descriptor.Path, resourcePath);
    }

    public void Load(DashboardScriptArguments args, bool forceReload = false) {
        // check if resources loaded, if loaded, just load the script,
        // NO !! YOU CANNOT JUST LOAD THE SCRIPT, YOU NEED TO RELOAD THE RESOURCES!!!
        // THE GRPAHICS DEVICE IS RECREATED when the game process ends and then starts, 
        // THE RESOURCES NEED TO BE RELOADED!!! because the factory used to create the image is different!!
        // AND if factories are different, Error 0x88990012 | D2DERR_WRONG_FACTORY will be thrown !!!
        if (IsLoaded) {
            Unload();
        }

        if (forceReload || this.ImageResources == null) {
            loadImageResources(args);
        }

        // load lua script
        _lua = new Lua();
        LuaG = _lua.CreateEnvironment();
        LuaG.DefaultCompileOptions = new LuaCompileOptions()
        {
            ClrEnabled = false,
            
        };
        foreach (var commonLuaScript in Directory.GetFiles(AppLevelVariables.Instance.GetPath(Path.Join(Constants.PATH_DASHBOARDS, "common")), "*.lua")) {
            LuaG.DoChunk(File.ReadAllText(commonLuaScript), $"{Guid.NewGuid()}.lua");
        }
        try {
            LuaG.DoChunk(File.ReadAllText(Path.Combine(Descriptor.Path, Constants.FILE_LUA_SCRIPT)), $"{Guid.NewGuid
        ()}.lua");
        } catch (Exception e) {
            if (e is LuaParseException ex) {
                _logger.Error("Lua parsing error at line {0}, column {1}: {2}", ex.Line, ex.Column, ex);
            }
        }
        args.Self = this;

        try {
            LuaG.CallMember("onInit", args);
        } catch (Exception e) {
            if (e is LuaRuntimeException ex) {
                _logger.Error("onInit, Lua runtime error at line {0}, column {1}: {2}", ex.Line, ex.Column, ex);
            }
        }
        _logger.Info($"Dashboard \"{I18NLoader.Instance[Descriptor.Name]}\" loaded");
    }

    public void Render(DashboardScriptArguments args) {
        if (!IsLoaded) {
            return;
        }

        args.Self = this;
        // render lua script
        try {
            LuaG.CallMember("onUpdate", args);
        } catch (Exception e) {
            if (e is LuaRuntimeException ex) {
                _logger.Error("onUpdate, Lua runtime error at line {0}, column {1}: {2}", ex.Line, ex.Column, ex);
            }
        }
    }

    public void Unload() {
        if (!IsLoaded) {
            return;
        }

        try {
            LuaG.CallMember("onExit");
        } catch (Exception e) {
            if (e is LuaRuntimeException ex) {
                _logger.Error("onExit, Lua runtime error at line {0}, column {1}: {2}", ex.Line, ex.Column, ex);
            }
        }
        _logger.Info($"Dashboard {I18NLoader.Instance[Descriptor.Name]} unloaded");
        LuaG.Clear();
        LuaG = null;
        _lua?.Dispose();
        _lua = null;
    }

    public object GetConfigByKey(string key) {
        if (DashboardConfigurations.PropertyName.ContainsKey(key))
            return DashboardConfigurations.PropertyValue[DashboardConfigurations.PropertyName.Keys.ToList().IndexOf(key)];
        return null;
    }

    public void SaveConfig() {
        File.WriteAllText(Path.Combine(Descriptor.Path, Constants.FILE_USER_SETTINGS), JsonConvert.SerializeObject(DashboardConfigurations, Formatting.Indented));
        Interlocked.Increment(ref _configurationRevision);
    }
}

public class DashboardResourceImageDescriptor {
    public string Path {set;get;}
    public string FormatGUID {set;get;}

    public object GetImage(object graphics, string resourceKey, string resourcePath) {
        if (graphics is Graphics nativeGraphics) {
            if (!string.IsNullOrEmpty(FormatGUID)) {
                return new Image(nativeGraphics, resourcePath, new Guid[] {Guid.Parse(FormatGUID)});
            }

            return new Image(nativeGraphics, resourcePath);
        }

        if (graphics is GraphicsCommandProxy webGraphics) {
            return webGraphics.CreateImage(resourceKey, resourcePath);
        }

        throw new InvalidOperationException("Dashboard resources require a supported graphics context.");
    }
}

public sealed record DashboardImageResourceSource(string Path, string FormatGuid);

public class DashboardDescriptor {
    public string Name { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public string Version { get; set; }
    public string PreviewImagePath { get; set; } // use wpf to render the preview image
    public Dictionary<string, DashboardResourceImageDescriptor> ImageResources { get; set; }

    public Dictionary<string, DashboardResourceImageDescriptor> ImageResourcesInDirectory { get; set; }
    public bool SupportsNativeStaticImageCache { get; set; }
    public bool SupportsWebDashboardClientLua { get; set; }
    public string Path { get; set; }
    public bool IsEnabled { get; set; } = true;
}
