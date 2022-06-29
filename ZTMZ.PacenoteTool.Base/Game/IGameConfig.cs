using System;
using System.Net;

namespace ZTMZ.PacenoteTool.Base.Game;

public interface IGameConfig 
{
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

    public static string Name => "udp";
}

