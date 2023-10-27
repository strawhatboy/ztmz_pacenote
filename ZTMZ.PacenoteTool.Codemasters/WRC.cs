using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class WRC : IGame
    {

        public string WindowTitle => "WRC";

        public string Name => GameName;
        public static string GameName = "EA Sports WRC";

        public string Description { get; private set; } = "";

        public string Executable => "wrc";

        public IGamePacenoteReader GamePacenoteReader { get; } = new BasePacenoteReader();

        public IGameDataReader GameDataReader { get; } = new DirtGameDataReader();

        public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/wrc.jpg";
        
        public Dictionary<string, IGameConfig> GameConfigurations { set; get; }

        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 2000;

        public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new DirtGamePrerequisiteChecker();

        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new Dictionary<string, IGameConfig>() 
        {
            { UdpGameConfig.Name, new UdpGameConfig() { IPAddress = System.Net.IPAddress.Loopback.ToString(), Port = 20777 } } 
        };

        public WRC()
        {
            this.Description = I18NLoader.Instance["game.wrc.description"];
            this.GameConfigurations = DefaultGameConfigurations;
        }
    }
}
