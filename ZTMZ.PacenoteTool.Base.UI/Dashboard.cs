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
    public CommonGameConfigs DashboardConfigurations { set; get; }

    public Dashboard(DashboardDescriptor descriptor) {
        Descriptor = descriptor;
    }

    public Dashboard(string jsonDescriptorPath) {
        Descriptor = JsonConvert.DeserializeObject<DashboardDescriptor>(File.ReadAllText(jsonDescriptorPath));
        Descriptor.Path = Path.GetDirectoryName(jsonDescriptorPath);
        DashboardConfigurations = JsonConvert.DeserializeObject<CommonGameConfigs>(File.ReadAllText(Path.Combine(Descriptor.Path, Constants.FILE_SETTINGS)));
    }

    public Image PreviewImage { get; set; }
    public Dictionary<string, Image> ImageResources { get; set; }

    private LuaGlobal LuaG { get; set; }

    public void Load(DashboardScriptArguments args) {
        // load resources
        if (!string.IsNullOrEmpty(Descriptor.PreviewImagePath))
            this.PreviewImage = new Image(args.Graphics, Descriptor.PreviewImagePath);

        this.ImageResources = new Dictionary<string, Image>();
        foreach (var imageResource in Descriptor.ImageResources) {
            this.ImageResources.Add(imageResource.Key, new Image(args.Graphics, imageResource.Value));
        }
        // load lua script
        using (var lua = new Lua()) {
            LuaG = lua.CreateEnvironment();
            LuaG.DefaultCompileOptions = new LuaCompileOptions()
            {
                ClrEnabled = false
            };
            LuaG.DoChunk(File.ReadAllText(Path.Combine(Descriptor.Path, Constants.FILE_LUA_SCRIPT)), $"{Guid.NewGuid()}.lua");
            args.Self = this;
            LuaG.CallMember("onInit", args);
            _logger.Info($"Dashboard \"{I18NLoader.Instance[Descriptor.Name]}\" loaded");
        }
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
        File.WriteAllText(Path.Combine(Descriptor.Path, Constants.FILE_SETTINGS), JsonConvert.SerializeObject(DashboardConfigurations, Formatting.Indented));
        // save IsEnabled in descriptor
        File.WriteAllText(Path.Combine(Descriptor.Path, Constants.DASHBOARD_INFO_FILE_NAME), JsonConvert.SerializeObject(Descriptor, Formatting.Indented));
    }
}

public class DashboardDescriptor {
    public string Name { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public string Version { get; set; }
    public string PreviewImagePath { get; set; }
    public Dictionary<string, string> ImageResources { get; set; }
    public string Path { get; set; }
    public bool IsEnabled { get; set; } = true;
}

