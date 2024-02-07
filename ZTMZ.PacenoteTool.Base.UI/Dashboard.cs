using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
}

public class DashboardScriptArguments {
    public Graphics Graphics { get; set; }
    public Config Config { get; set; }
    public I18NLoader I18NLoader { get; set; }
    public GameData GameData { get; set; }
    public GameContext GameContext { get; set; }

    public Dashboard Self { get; set; }

    public GameOverlayDrawingHelper GameOverlayDrawingHelper { get; set; }
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

    public DashboardDescriptor Descriptor { get; set; }

    // reuse CommonGameConfig as dashboard configuration :) settings.json
    public DashboardConfigs DashboardConfigurations { set; get; }

    public Dashboard(DashboardDescriptor descriptor) {
        Descriptor = descriptor;
    }

    public Dashboard(string jsonDescriptorPath) {
        Descriptor = JsonConvert.DeserializeObject<DashboardDescriptor>(File.ReadAllText(jsonDescriptorPath));
        Descriptor.Path = Path.GetDirectoryName(jsonDescriptorPath);
        loadConfig();
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

    public void SetIsEnable(bool value) {
        Descriptor.IsEnabled = value;
        DashboardConfigurations["dashboards.settings.enabled"] = value;
    }

    public Image PreviewImage { get; set; }
    public Dictionary<string, Image> ImageResources { get; set; }

    private LuaGlobal LuaG { get; set; }

    private Lua _lua = new Lua();

    private void loadImageResources(DashboardScriptArguments args) {
        // load resources
        if (Descriptor.PreviewImage != null && !string.IsNullOrEmpty(Descriptor.PreviewImage.Path))
            this.PreviewImage = Descriptor.PreviewImage.GetImage(args.Graphics);

        this.ImageResources = new Dictionary<string, Image>();
        foreach (var imageResource in Descriptor.ImageResources) {
            imageResource.Value.Path = Path.Combine(Descriptor.Path, imageResource.Value.Path);
            this.ImageResources.Add(imageResource.Key, imageResource.Value.GetImage(args.Graphics));
        }

        foreach (var imageResourceDirectory in Descriptor.ImageResourcesInDirectory) {
            // imageResourceDirectory.Value is the ImageDescriptor contains the path of images and formatGUID
            if (imageResourceDirectory.Value == null) continue;

            var directory = Path.Combine(Descriptor.Path, imageResourceDirectory.Value.Path);
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
                this.ImageResources.Add(imageResourceDirectory.Key + "@" + Path.GetFileNameWithoutExtension(file), imageDescriptor.GetImage(args.Graphics));
            }
        }
    }

    public void Load(DashboardScriptArguments args) {
        // check if resources loaded, if loaded, just load the script
        if (this.ImageResources == null) {
            loadImageResources(args);
        }

        // load lua script
        LuaG = _lua.CreateEnvironment();
        LuaG.DefaultCompileOptions = new LuaCompileOptions()
        {
            ClrEnabled = false
        };
        LuaG.DoChunk(File.ReadAllText(Path.Combine(Descriptor.Path, Constants.FILE_LUA_SCRIPT)), $"{Guid.NewGuid()}.lua");
        args.Self = this;
        LuaG.CallMember("onInit", args);
        _logger.Info($"Dashboard \"{I18NLoader.Instance[Descriptor.Name]}\" loaded");
    }

    public void Render(DashboardScriptArguments args) {
        args.Self = this;
        // render lua script
        LuaG.CallMember("onUpdate", args);
    }

    public void Unload() {
        LuaG.CallMember("onExit");
        _logger.Info($"Dashboard {I18NLoader.Instance[Descriptor.Name]} unloaded");
        LuaG.Clear();
    }

    public object GetConfigByKey(string key) {
        if (DashboardConfigurations.PropertyName.ContainsKey(key))
            return DashboardConfigurations.PropertyValue[DashboardConfigurations.PropertyName.Keys.ToList().IndexOf(key)];
        return null;
    }

    public void SaveConfig() {
        File.WriteAllText(Path.Combine(Descriptor.Path, Constants.FILE_USER_SETTINGS), JsonConvert.SerializeObject(DashboardConfigurations, Formatting.Indented));
    }
}

public class DashboardResourceImageDescriptor {
    public string Path {set;get;}
    public string FormatGUID {set;get;}

    public Image GetImage(Graphics graphics) {
        if (!string.IsNullOrEmpty(FormatGUID)) {
            return new Image(graphics, Path, new Guid[] {Guid.Parse(FormatGUID)});
        } else {
            return new Image(graphics, Path);
        }
    }
}

public class DashboardDescriptor {
    public string Name { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public string Version { get; set; }
    public DashboardResourceImageDescriptor PreviewImage { get; set; }
    public Dictionary<string, DashboardResourceImageDescriptor> ImageResources { get; set; }

    public Dictionary<string, DashboardResourceImageDescriptor> ImageResourcesInDirectory { get; set; }
    public string Path { get; set; }
    public bool IsEnabled { get; set; } = true;
}

