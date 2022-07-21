using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class DirtRally : IGame
    {
        public string WindowTitle => "Dirt Rally";

        public string Name => GameName;

        public static string GameName = "Dirt Rally 1.0";

        public string Description => "";

        public string Executable => "drt.exe";
        public IGamePacenoteReader GamePacenoteReader {get;} = new BasePacenoteReader();

        public IGameDataReader GameDataReader { set; get; } = new DirtGameDataReader();
        
        public BitmapImage Image { get; } = new BitmapImage(new Uri("pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/dirtrally.jpg"));
        
        public Dictionary<string, IGameConfig> GameConfigurations { set; get; }

        public bool IsRunning { get; set; }

        public int Order => 1000;

        public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new DirtGamePrerequisiteChecker();
        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new Dictionary<string, IGameConfig>() 
        {
            { UdpGameConfig.Name, new UdpGameConfig() { IPAddress = System.Net.IPAddress.Loopback.ToString(), Port = 20777 } } 
        };

        public DirtRally()
        {
            this.GameConfigurations = DefaultGameConfigurations;
        }
    }
}
