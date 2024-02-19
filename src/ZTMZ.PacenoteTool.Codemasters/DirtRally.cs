using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class DirtRally : IGame
    {
        public string WindowTitle => "DiRT Rally";

        public string Name => GameName;

        public static string GameName = "Dirt Rally 1.0";

        public string Description { get; private set; } = "";

        public string Executable => "dirt";
        public IGamePacenoteReader GamePacenoteReader {get;} = new BasePacenoteReader();

        public IGameDataReader GameDataReader { set; get; } = new DirtGameDataReader();
        
        public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/dirtrally.jpg";
        
        public Dictionary<string, IGameConfig> GameConfigurations { set; get; }

        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }

        public int Order => 1000;

        public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new DirtGamePrerequisiteChecker();
        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new Dictionary<string, IGameConfig>() 
        {
            { UdpGameConfig.Name, new UdpGameConfig() { IPAddress = System.Net.IPAddress.Loopback.ToString(), Port = 20777 } } 
        };

        public DirtRally()
        {
            this.Description = I18NLoader.Instance["game.dr1.description"];
            this.GameConfigurations = DefaultGameConfigurations;
        }
    }
}
