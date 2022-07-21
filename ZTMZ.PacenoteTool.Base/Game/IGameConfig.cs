using System;
using System.Net;
using System.Windows.Controls;
using Newtonsoft.Json;

namespace ZTMZ.PacenoteTool.Base.Game;

public interface IGameConfig
{
    UserControl UI { get; set; }
}

public class GameConfigAttribute : Attribute
{
    public string name;

    public GameConfigAttribute(string name) 
    {
        this.name = name;
    }
}

[GameConfigAttribute("udp")]
public class UdpGameConfig : IGameConfig
{
    public string IPAddress { set; get; }
    public int Port { set; get; }

    [JsonIgnore]
    public UserControl UI { set; get; }

    public static string Name => "udp";

    public UdpGameConfig()
    {
        UI = new UdpGameConfigSettingsPane(this);
    }
}

[GameConfig("memory")]
public class MemoryGameConfig : IGameConfig
{
    public float RefreshRate { set; get; }

    public static string Name => "memory";

    [JsonIgnore]
    public UserControl UI { set; get; }

    public MemoryGameConfig()
    {
        UI = new MemoryGameConfigSettingsPane(this);
    }
}

