using System;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemaster
{
    public class DirtRally2 : IGame
    {
        /// <summary>
        /// Game window title name, can be used for retrieving the window handle
        /// </summary>
        public string WindowTitle => "Dirt Rally 2.0";

        /// <summary>
        /// Name of the game
        /// </summary>
        public string Name => "Dirt Rally 2.0";

        /// <summary>
        /// Description of the game
        /// </summary>
        public string Description => "";

        /// <summary>
        /// Executable file name for checking the path legality
        /// </summary>
        public string Executable => "dirtrally2";

        /// <summary>
        /// Pacenote Reader to load pacenote by track name or track No.
        /// </summary>
        public IGamePacenoteReader GamePacenoteReader => new BasePacenoteReader();

        /// <summary>
        /// UDPAnalyzer to analyze the UDP package and returns essential information
        /// </summary>
        public IGameUDPAnalyazer GameUdpAnalyazer { set; get; }
        public BitmapImage Image => new BitmapImage(new Uri("pack://application:,,,/ZTMZ.PacenoteTool.Codemaster;component/dirtrally2.jpg"));

        public DirtRally()
        {
            
        }
    }
}
