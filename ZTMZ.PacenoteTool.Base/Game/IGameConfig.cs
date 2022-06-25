using System.Net;

namespace ZTMZ.PacenoteTool.Base.Game;

public interface IGameConfig 
{
}

public class UdpGameConfig : IGameConfig
{
    public IPAddress IPAddress { set; get; }
    public int Port { set; get; }

    public static string Name => "udp";
}

