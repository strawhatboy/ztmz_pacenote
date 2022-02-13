namespace ZTMZ.PacenoteTool.Base.Game
{
    public interface IGame
    {
        /// <summary>
        /// Game window title name, can be used for retrieving the window handle
        /// </summary>
        string WindowTitle { set; get; }

        /// <summary>
        /// Name of the game
        /// </summary>
        string Name { set; get; }

        /// <summary>
        /// Description of the game
        /// </summary>
        string Description { set; get; }

        /// <summary>
        /// Executable file name for checking the path legality
        /// </summary>
        string Executable { set; get; }

        /// <summary>
        /// Pacenote Reader to load pacenote by track name or track No.
        /// </summary>
        IGamePacenoteReader GamePacenoteReader { set; get; }

        /// <summary>
        /// UDPAnalyzer to analyze the UDP package and returns essential information
        /// </summary>
        IGameUDPAnalyazer GameUdpAnalyazer { set; get; }
    }
}