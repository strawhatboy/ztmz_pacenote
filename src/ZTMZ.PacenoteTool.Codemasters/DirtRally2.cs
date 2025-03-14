using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class DirtRally2 : IGame
    {

        public string WindowTitle => "DiRT Rally 2.0";

        public string Name => GameName;
        public static string GameName = "Dirt Rally 2.0";

        public event Action<int, string> onCustomMessage;

        public string Description { get; private set; } = "";

        public string Executable => "dirtrally2";

        public IGamePacenoteReader GamePacenoteReader { get; } = new BasePacenoteReader();

        public IGameDataReader GameDataReader { get; } = new DirtGameDataReader();

        public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/dirtrally2.jpg";
        
        public Dictionary<string, IGameConfig> GameConfigurations { set; get; }

        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 2000;

        public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new DirtGamePrerequisiteChecker();

        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new Dictionary<string, IGameConfig>() 
        {
            { UdpGameConfig.Name, new UdpGameConfig() { IPAddress = System.Net.IPAddress.Loopback.ToString(), Port = 59996 } } 
        };

        public DirtRally2()
        {
            this.Description = I18NLoader.Instance["game.dr2.description"];
            this.GameConfigurations = DefaultGameConfigurations;
        }

        public void OnCustomMessage(int level, string message)
        {
            onCustomMessage?.Invoke(level, message);
        }
    }
}
