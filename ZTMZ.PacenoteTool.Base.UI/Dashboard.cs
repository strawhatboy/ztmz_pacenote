using System;
using System.Collections.Generic;
using System.IO;
using GameOverlay.Drawing;
using Neo.IronLua;
using Newtonsoft.Json;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base.UI;

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

    public Dashboard(DashboardDescriptor descriptor) {
        Descriptor = descriptor;
    }

    public Dashboard(string jsonDescriptorPath) {
        Descriptor = JsonConvert.DeserializeObject<DashboardDescriptor>(File.ReadAllText(jsonDescriptorPath));
    }

    public Image PreviewImage { get; set; }
    public Dictionary<string, Image> ImageResources { get; set; }

    private LuaGlobal LuaG { get; set; }

    public void Load(Graphics graphics) {
        // load resources
        if (!string.IsNullOrEmpty(Descriptor.PreviewImagePath))
            this.PreviewImage = new Image(graphics, Descriptor.PreviewImagePath);

        this.ImageResources = new Dictionary<string, Image>();
        foreach (var imageResource in Descriptor.ImageResources) {
            this.ImageResources.Add(imageResource.Key, new Image(graphics, imageResource.Value));
        }
        // load lua script
        using (var lua = new Lua()) {
            LuaG = lua.CreateEnvironment();
            LuaG.DefaultCompileOptions = new LuaCompileOptions()
            {
                ClrEnabled = false
            };
            LuaG.DoChunk(File.ReadAllText(Path.Combine(Descriptor.Path, Descriptor.LuaScriptPath)), $"{Guid.NewGuid()}.lua");
            LuaG.CallMember("onInit", graphics, Config.Instance);
            _logger.Info($"Dashboard \"{Descriptor.Name}\" loaded");
        }
    }

    public void Render(Graphics graphics, GameData gameData) {
        // render lua script
        LuaG.CallMember("onUpdate", graphics, Config.Instance, gameData);
    }

    public void Unload() {
        LuaG.CallMember("onExit");
        _logger.Info($"Dashboard {Descriptor.Name} unloaded");
    }
}

public class DashboardDescriptor {
    public string Name { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public string Version { get; set; }
    public string PreviewImagePath { get; set; }
    public string LuaScriptPath { get; set; }
    public Dictionary<string, string> ImageResources { get; set; }
    public string Path { get; set; }
}

