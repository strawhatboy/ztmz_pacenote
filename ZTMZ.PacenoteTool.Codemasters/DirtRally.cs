using System;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class DirtRally : IGame
    {
        public string WindowTitle => "Dirt Rally";

        public string Name => "Dirt Rally 1.0";

        public string Description => "";

        public string Executable => "dirtrally";

        public IGamePacenoteReader GamePacenoteReader => new BasePacenoteReader();

        public IGameDataReader GameDataReader { set; get; }
        
        public BitmapImage Image => new BitmapImage(new Uri("pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/dirtrally.jpg"));

        public DirtRally()
        {
            
        }
    }
}
