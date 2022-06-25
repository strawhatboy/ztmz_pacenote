

using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
namespace ZTMZ.PacenoteTool.Base.Game
{
    public interface IGame
    {
        /// <summary>
        /// Game window title name, can be used for retrieving the window handle
        /// </summary>
        string WindowTitle { get; }

        /// <summary>
        /// Name of the game
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of the game
        /// </summary>
        string Description { get; }

        BitmapImage Image { get; }

        /// <summary>
        /// Executable file name for checking the path legality
        /// </summary>
        string Executable { get; }

        /// <summary>
        /// Pacenote Reader to load pacenote by track name or track No.
        /// </summary>
        IGamePacenoteReader GamePacenoteReader { get; }

        /// <summary>
        /// GameDataReader to analyze the UDP package or read memory to return essential information
        /// </summary>
        IGameDataReader GameDataReader { get; }

        Dictionary<string, IGameConfig> GameConfigurations { get; }


    }
}
