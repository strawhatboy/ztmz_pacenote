using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class DirtRally2 : IGame
    {

        public string WindowTitle => "Dirt Rally 2.0";

        public string Name => "Dirt Rally 2.0";

        public string Description => "";

        public string Executable => "dirtrally2.exe";

        public IGamePacenoteReader GamePacenoteReader { get; } = new BasePacenoteReader();

        public IGameDataReader GameDataReader { get; } = new DirtGameDataReader();

        public BitmapImage Image { get; } = new BitmapImage(new Uri("pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/dirtrally2.jpg"));
        
        public Dictionary<string, IGameConfig> GameConfigurations { set; get; } = new Dictionary<string, IGameConfig>() 
        {
            { UdpGameConfig.Name, new UdpGameConfig() { IPAddress = System.Net.IPAddress.Loopback.ToString(), Port = 20777 } } 
        };

        public bool IsRunning { get; set; }
        public int Order => 2000;

        public DirtRally2()
        {
        }
    }
}
