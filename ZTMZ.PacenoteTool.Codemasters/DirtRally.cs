using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class DirtRally : IGame
    {
        public string WindowTitle => "Dirt Rally";

        public string Name => "Dirt Rally 1.0";

        public string Description => "";

        public string Executable => "dirtrally.exe";
        public IGamePacenoteReader GamePacenoteReader {get;} = new BasePacenoteReader();

        public IGameDataReader GameDataReader { set; get; } = new DirtGameDataReader();
        
        public BitmapImage Image => new BitmapImage(new Uri("pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/dirtrally.jpg"));
        
        public Dictionary<string, IGameConfig> GameConfigurations => new Dictionary<string, IGameConfig>() 
        {
            { UdpGameConfig.Name, new UdpGameConfig() { IPAddress = System.Net.IPAddress.Loopback, Port = 20777 } } 
        };

        public bool IsRunning { get; set; }

        public DirtRally()
        {
            
        }
    }
}
