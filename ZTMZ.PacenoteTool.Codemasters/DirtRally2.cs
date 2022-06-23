using System;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class DirtRally2 : IGame
    {
        public string WindowTitle => "Dirt Rally 2.0";

        public string Name => "Dirt Rally 2.0";

        public string Description => "";

        public string Executable => "dirtrally2";

        public IGamePacenoteReader GamePacenoteReader => new BasePacenoteReader();

        public IGameDataReader GameDataReader { get; }

        public BitmapImage Image => new BitmapImage(new Uri("pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/dirtrally2.jpg"));

        public DirtRally2()
        {
        }
    }
}
