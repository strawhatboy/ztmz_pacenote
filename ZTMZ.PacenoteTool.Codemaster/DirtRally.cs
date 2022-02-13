using System;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemaster
{
    public class DirtRally : IGame
    {
        /// <summary>
        /// Game window title name, can be used for retrieving the window handle
        /// </summary>
        public string WindowTitle { set; get; } = "Dirt Rally";

        /// <summary>
        /// Name of the game
        /// </summary>
        public string Name { set; get; } = "Dirt Rally 1.0";

        /// <summary>
        /// Description of the game
        /// </summary>
        public string Description { set; get; } = "";

        /// <summary>
        /// Executable file name for checking the path legality
        /// </summary>
        public string Executable { set; get; } = "dirtrally";

        /// <summary>
        /// Pacenote Reader to load pacenote by track name or track No.
        /// </summary>
        public IGamePacenoteReader GamePacenoteReader { set; get; }

        /// <summary>
        /// UDPAnalyzer to analyze the UDP package and returns essential information
        /// </summary>
        public IGameUDPAnalyazer GameUdpAnalyazer { set; get; }
        
        public DirtRally()
        {
            
        }
    }
}