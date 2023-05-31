

using System;
using System.Collections.Generic;
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

        string ImageUri { get; }

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

        IGamePrerequisiteChecker GamePrerequisiteChecker { get; }

        /// <summary>
        /// GameConfigurations, need to be saved to userconfig.json? or a separated file?
        /// </summary>
        Dictionary<string, IGameConfig> GameConfigurations { set; get; }
        Dictionary<string, IGameConfig> DefaultGameConfigurations { get; }



        bool IsRunning { set; get; }

        int Order { get; }
    }
}
