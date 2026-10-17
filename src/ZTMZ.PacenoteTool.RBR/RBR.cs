
// RBR root is derived from the player's configured executable, with the registry as a legacy fallback.
// RBR track name by track id: [rbrRoot]\Maps\Tracks.ini (DLS tracks)
// Analyze BTB tracks from [rbrRoot]\RX_CONTENT\Tracks\[TrackName]\[TrackId]_[TrackName].jpg
//
// ==== Pacenotes ====
//      Use the latest pacenote for BTB track, use corresponding M,N,E,O pacenote for DLS track.
//      But check custom pacenote first
// ===================
//
// ---- Custom pacenote for track 
// [rbrRoot]\Plugins\NGPCarMenu.ini\[MyPacenotesPath] <disabled>|[PATH]
// [MyPacenotesPath]\[TrackName] [DateTime].ini
//      need to check [MyPacenotesPath]\mypacenote_[PacenoteFileNameWOExtension].txt, can be default|latest|[CustomPacenoteFilename]
// ---- DLS pacenote for track
//  [rbrRoot]\Maps\[Track_ID]-[TrackName]\track-[TrackID]_[M|N|E|O].dls
//  or [rbrRoot]\Maps\track-[TrackID]_[M|N|E|O].dls
// ---- BTB pacenote for track (RX Tracks)
//  [rbrRoot]\RX_CONTENT\Tracks\[TrackName]\pacenotes[*].ini
//
// ---- DLS pacenote rules
//      1. check 0x38, if it's 1, then
//          pacenotecount: 0x5C, pacenotedata_addr: 0x7C (LE)
//      2. if 0x38 is not 1, then
//          pacenotecount: value before 00 00 00 00 1C 00 00 00
//          pacenotedata_addr: &pacenotecount + 0x20
//      3. read pacenotes
//          type:       0x00
//          modifier:   0x04
//          distance:   0x08

// ---- Get Track ID From Memory
//      1. read Int32 x from 0x7EA678
//      2. read Int32 y from x + 112
//      3. read Int32 z from y + 32
//      4. z is the Track ID.

// ---- Get TrackName by ID From Memory
//      1. read Int32 x from 0x4A1123
//      2. if x is 0x731234 or 0x0
//          read 2 bytes char from address x till got \0 or totally 128 bytes
//      3. else try to get the TrackName from Tracks.ini or RX_CONTENT files
//      

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR
{
    public class RBR : IGame, IGameConfigurationMigrator, IGameWithIntegrationProviders,
        IGamePacenoteAudioProvider, IGameWithModCatalog
    {
        internal const string UseInstalledCodriverAudioConfigKey =
            "game.rbr.additional_settings.useInstalledCodriverAudio";

        private readonly RBRInstalledCodriverAudioProvider _installedCodriverAudioProvider = new();
        public string WindowTitle => "Richard Burns Rally";

        public string Name => GameName;
        
        public static string GameName = "Richard Burns Rally - RSF";

        public string Description { get; private set; } = "";

        public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.RBR;component/rbr.jpg";

        public string Executable => "RichardBurnsRally_SSE";

        public IGamePacenoteReader GamePacenoteReader { get; } = new RBRGamePacenoteReader();

        public IGameDataReader GameDataReader { get; } = new RBRGameDataReader();

        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }

        private Dictionary<string, IGameConfig> _gameConfigurations;

        public event Action<int, string> onCustomMessage;

        public Dictionary<string, IGameConfig> GameConfigurations { 
            set {
                _gameConfigurations = value;
                if (_gameConfigurations.TryGetValue(RBRGameInstallationConfig.Name, out var installationConfiguration))
                {
                    ((RBRGamePacenoteReader)GamePacenoteReader).Configure(
                        installationConfiguration as RBRGameInstallationConfig);
                }
                // load script resource
                var commonConfiguration = (CommonGameConfigs)_gameConfigurations[CommonGameConfigs.Name];
                var pacenoteDef = Convert.ToString(
                    commonConfiguration["game.rbr.additional_settings.additional_pacenote_def"]) ?? string.Empty;
                RBRScriptResource.Instance.LoadData(pacenoteDef).GetAwaiter().GetResult();
                _installedCodriverAudioProvider.Configure(
                    installationConfiguration as RBRGameInstallationConfig,
                    commonConfiguration[UseInstalledCodriverAudioConfigKey] is true);
            }
            get {
                return _gameConfigurations;
            }
        }
        
        public int Order => 3000;

        public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new RBRGamePrerequisiteChecker();

        public IReadOnlyList<IGameIntegrationProvider> IntegrationProviders { get; } =
        [
            new RBRGameInstallationIntegrationProvider()
        ];

        public string ModCatalogGameId => "rbr";

        public Uri ModRepositoryUri { get; } =
            new("https://gitee.com/ztmz/game-mods/raw/master/repo.json");

        public IGameModManager ModManager { get; } = new RBRModManager();

        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new() {
            { UdpGameConfig.Name, new UdpGameConfig() { IPAddress = System.Net.IPAddress.Loopback.ToString(), Port = 30001 } },
            { MemoryGameConfig.Name, new MemoryGameConfig() { RefreshRate = 60f } },    // use 60Hz as default
            { RBRGameInstallationConfig.Name, new RBRGameInstallationConfig() },
            { CommonGameConfigs.Name, new CommonGameConfigs() 
            { 
                PropertyName = new() {
                    { "game.rbr.additional_settings.playPacenotesWhenReplaying", "game.rbr.additional_settings.tooltip.playPacenotesWhenReplaying" },
                    { "game.rbr.additional_settings.additional_pacenote_def", "game.rbr.additional_settings.tooltip.additional_pacenote_def" },
                    { "game.rbr.additional_settings.autoSwitchPacenoteDef", "game.rbr.additional_settings.tooltip.autoSwitchPacenoteDef"},
                    { UseInstalledCodriverAudioConfigKey, "game.rbr.additional_settings.tooltip.useInstalledCodriverAudio" }
                },
                PropertyValue = new() {
                    true,
                    AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, "jannemod_v3.zdb")),   // custom pacenote
                    true,
                    false
                },
                PropertyType = new() {
                    "bool",
                    "file:zdb",
                    "bool",
                    "bool"
                }
            }},
        };

        public RBR() 
        {
            this.Description = I18NLoader.Instance["game.rbr.description"];
            GameConfigurations = DefaultGameConfigurations;
        }

        public bool MigrateGameConfiguration()
        {
            if (!_gameConfigurations.TryGetValue(CommonGameConfigs.Name, out var configuration) ||
                configuration is not CommonGameConfigs commonConfig ||
                DefaultGameConfigurations[CommonGameConfigs.Name] is not CommonGameConfigs defaultConfig)
            {
                return false;
            }

            return RBRGameConfigurationMigration.Migrate(commonConfig, defaultConfig);
        }

        public void OnCustomMessage(int level, string message)
        {
            onCustomMessage?.Invoke(level, message);
        }

        public void PreparePacenoteAudio() =>
            _installedCodriverAudioProvider.PreparePacenoteAudio();

        public bool TryResolvePacenoteAudio(
            GamePacenoteAudioCue cue,
            out GamePacenoteAudioResolution resolution) =>
            _installedCodriverAudioProvider.TryResolvePacenoteAudio(cue, out resolution);

        public bool TryResolveSystemAudio(
            string systemSoundToken,
            out GamePacenoteAudioSegment segment) =>
            _installedCodriverAudioProvider.TryResolveSystemAudio(systemSoundToken, out segment);
    }

    internal static class RBRGameConfigurationMigration
    {
        internal const string DamageTelemetryDiagnosticsConfigKey = "game.rbr.additional_settings.damageTelemetryDiagnostics";

        public static bool Migrate(CommonGameConfigs commonConfig, CommonGameConfigs defaultConfig)
        {
            var changed = false;
            var damageTelemetryDiagnosticsIndex = commonConfig.PropertyName.Keys
                .ToList()
                .IndexOf(DamageTelemetryDiagnosticsConfigKey);
            if (damageTelemetryDiagnosticsIndex >= 0)
            {
                commonConfig.PropertyName.Remove(DamageTelemetryDiagnosticsConfigKey);
                RemoveAtIfPresent(commonConfig.PropertyValue, damageTelemetryDiagnosticsIndex);
                RemoveAtIfPresent(commonConfig.PropertyType, damageTelemetryDiagnosticsIndex);
                RemoveAtIfPresent(commonConfig.ValueRange, damageTelemetryDiagnosticsIndex);
                changed = true;
            }

            var defaultTypes = defaultConfig.PropertyName.Keys
                .Select((key, index) => new { key, type = defaultConfig.PropertyType[index] })
                .ToDictionary(item => item.key, item => item.type, StringComparer.Ordinal);
            var migratedTypes = commonConfig.PropertyName.Keys
                .Select((key, index) => defaultTypes.TryGetValue(key, out var type)
                    ? type
                    : index < commonConfig.PropertyType.Count ? commonConfig.PropertyType[index] : string.Empty)
                .ToList();
            if (!commonConfig.PropertyType.SequenceEqual(migratedTypes, StringComparer.Ordinal))
            {
                commonConfig.PropertyType = migratedTypes;
                changed = true;
            }

            return changed;
        }

        private static void RemoveAtIfPresent<T>(IList<T> values, int index)
        {
            if (index >= 0 && index < values.Count)
            {
                values.RemoveAt(index);
            }
        }
    }
}
