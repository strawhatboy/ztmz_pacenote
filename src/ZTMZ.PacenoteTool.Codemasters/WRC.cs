using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class WRC : IGame, ISteamGameIdentity, IGameWithResourceLinks
    {

        public string WindowTitle => "EA SPORTS™ WRC";

        public string Name => GameName;
        public static string GameName = "EA SPORTS™ WRC";
        public const uint SteamAppId = 1849250;
        public uint SteamApplicationId => SteamAppId;

        public IReadOnlyList<GameResourceLink> ResourceLinks { get; } =
        [
            new("replacement-chinese-pacenotes", "game.resources.replacementChinesePacenotes",
                new Uri("https://gitee.com/ztmz/ea_wrc_chinese_codrivers/releases"),
                GameResourceKind.Pacenotes,
                DescriptionKey: "game.resources.wrcReplacementChinesePacenotesDescription"),
            new("chinese-localization", "game.resources.chineseLocalization",
                new Uri("https://gitee.com/ztmz/ea_wrc_chinese_translation/releases"),
                GameResourceKind.Localization,
                DescriptionKey: "game.resources.wrcChineseLocalizationDescription")
        ];

        public event Action<int, string> onCustomMessage;

        public string Description { get; private set; } = "";

        public string Executable => "WRC";

        public IGamePacenoteReader GamePacenoteReader { get; } = new BasePacenoteReader();

        public IGameDataReader GameDataReader { get; } = new WRCGameDataReader();

        public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/wrc.jpg";
        
        public Dictionary<string, IGameConfig> GameConfigurations { set; get; }

        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 2000;

        public IGamePrerequisiteChecker GamePrerequisiteChecker { get; } = new WRCGamePrerequisiteChecker();

        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new Dictionary<string, IGameConfig>() 
        {
            { UdpGameConfig.Name, new UdpGameConfig() { IPAddress = System.Net.IPAddress.Loopback.ToString(), Port = 30002 } }
        };

        public WRC()
        {
            this.Description = I18NLoader.Instance["game.wrc.description"];
            this.GameConfigurations = DefaultGameConfigurations;
        }

        public void OnCustomMessage(int level, string message)
        {
            onCustomMessage?.Invoke(level, message);
        }
    }
}
