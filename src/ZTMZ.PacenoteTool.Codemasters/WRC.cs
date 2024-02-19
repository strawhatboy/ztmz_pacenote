using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class WRC : IGame
    {

        public string WindowTitle => "EA SPORTS™ WRC";

        public string Name => GameName;
        public static string GameName = "EA SPORTS™ WRC";

        public string Description { get; private set; } = "";

        public string Executable => "WRC";

        public IGamePacenoteReader GamePacenoteReader { get; } = new BasePacenoteReader();

        public IGameDataReader GameDataReader { get; } = new WRCGameDataReader();

        public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/wrc.jpg";
        
        public Dictionary<string, IGameConfig> GameConfigurations { set; get; }

        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 2000;

        public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new WRCGamePrerequisiteChecker();

        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new Dictionary<string, IGameConfig>() 
        {
            { UdpGameConfig.Name, new UdpGameConfig() { IPAddress = System.Net.IPAddress.Loopback.ToString(), Port = 60006 } } 
        };

        public WRC()
        {
            this.Description = I18NLoader.Instance["game.wrc.description"];
            this.GameConfigurations = DefaultGameConfigurations;
        }
    }
}
