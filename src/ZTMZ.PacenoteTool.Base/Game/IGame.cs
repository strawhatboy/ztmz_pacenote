

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        event Action<int, string> onCustomMessage;
        void OnCustomMessage(int level, string message);

        bool IsRunning { set; get; }

        bool IsInitialized {set;get;}

        int Order { get; }
    }

    /// <summary>
    /// Allows a game provider to keep the game's own co-driver audio authoritative.
    /// </summary>
    public interface IGamePacenotePlaybackControl
    {
        bool EnableExternalPacenotes { get; }
    }

    /// <summary>
    /// Lets a game provider migrate persisted settings after they have been loaded.
    /// </summary>
    public interface IGameConfigurationMigrator
    {
        bool MigrateGameConfiguration();
    }

    /// <summary>Optional Steam identity used by platform game discovery.</summary>
    public interface ISteamGameIdentity
    {
        uint SteamApplicationId { get; }
    }

    public sealed record LocalBridgeDiagnosticsInfo(
        string Name,
        bool IsEnabled,
        bool IsAvailable,
        bool IsInjected,
        string Detail);

    /// <summary>Optional local game bridge state exposed to the desktop diagnostics UI.</summary>
    public interface ILocalGameBridgeDiagnostics
    {
        LocalBridgeDiagnosticsInfo GetLocalBridgeDiagnostics();
    }

    public sealed record LocalBridgeInjectionResult(bool Succeeded, string Detail);

    /// <summary>Allows the desktop UI to request an explicitly opted-in local bridge injection.</summary>
    public interface ILocalGameBridgeControl : ILocalGameBridgeDiagnostics
    {
        Task<LocalBridgeInjectionResult> InjectLocalBridgeAsync();
    }

    /// <summary>Signals that a provider has completed a local bridge injection.</summary>
    public static class GameRuntimeNotifications
    {
        public static event Action<IGame> LocalBridgeInjected;
        public static event Action<IGame> LocalBridgeControlsEnabled;

        public static void NotifyLocalBridgeInjected(IGame game)
        {
            LocalBridgeInjected?.Invoke(game);
        }

        public static void NotifyLocalBridgeControlsEnabled(IGame game)
        {
            LocalBridgeControlsEnabled?.Invoke(game);
        }
    }
}
