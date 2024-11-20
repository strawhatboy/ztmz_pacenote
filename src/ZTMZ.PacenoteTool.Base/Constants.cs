using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base
{

    public class Constants
    {

        public static string DEFAULT_PROFILE = "default";
        public static string PATH_CODRIVERS = "codrivers";
        public static string DEFAULT_CODRIVER = "codrivers\\default";
        public static string CODRIVER_FILENAME = "codriver.txt";
        public static string CODRIVER_PACKAGE_INFO_FILENAME = "info.json";
        public static string DASHBOARD_INFO_FILE_NAME = "info.json";
        public static string PATH_GAMES = "games";
        public static string PATH_LANGUAGE = "lang";
        public static string PATH_FONTS = "fonts";
        public static string PATH_DASHBOARDS = "dashboards";
        public static string FILE_LUA_SCRIPT = "script.lua";
        public static string FILE_SETTINGS = "settings.json";

        public static string FILE_USER_SETTINGS = "user_settings.json";

        // system sound
        public const string SYSTEM_START_STAGE = "system_start_stage";
        public const string SYSTEM_END_STAGE = "system_end_stage";
        public const string SYSTEM_RETIRED = "system_retired";
        public const string SYSTEM_GO = "system_go";
        public const string SYSTEM_PUNCTURE_FRONT_LEFT = "system_puncture_front_left";
        public const string SYSTEM_PUNCTURE_FRONT_RIGHT = "system_puncture_front_right";
        public const string SYSTEM_PUNCTURE_REAR_LEFT = "system_puncture_rear_left";
        public const string SYSTEM_PUNCTURE_REAR_RIGHT = "system_puncture_rear_right";

        public const string SYSTEM_COLLISION_SLIGHT = "system_collision_slight";
        public const string SYSTEM_COLLISION_MEDIUM = "system_collision_medium";
        public const string SYSTEM_COLLISION_SEVERE = "system_collision_severe";

        public const string SYSTEM_COUNTDOWN_5 = "system_countdown_5";
        public const string SYSTEM_COUNTDOWN_4 = "system_countdown_4";
        public const string SYSTEM_COUNTDOWN_3 = "system_countdown_3";
        public const string SYSTEM_COUNTDOWN_2 = "system_countdown_2";
        public const string SYSTEM_COUNTDOWN_1 = "system_countdown_1";
        public const int PACENOTE_INTO = 114;
        public const int PACENOTE_ONTO = 327;
        public const int PACENOTE_AND = 29;

        public const string HUD_WINDOW_NAME = "ZTMZ Club Hud - main";

        public static Dictionary<string, string> SYSTEM_SOUND = new Dictionary<string, string>()
        {
            { SYSTEM_START_STAGE, "地图载入" },
            { SYSTEM_END_STAGE, "游戏结束" },
            { SYSTEM_GO, "比赛开始" },
            { SYSTEM_PUNCTURE_FRONT_LEFT, "左前轮爆胎" }, 
            { SYSTEM_PUNCTURE_FRONT_RIGHT, "右前轮爆胎" },  
            { SYSTEM_PUNCTURE_REAR_LEFT, "左后轮爆胎" },  
            { SYSTEM_PUNCTURE_REAR_RIGHT, "右后轮爆胎" }, 
            { SYSTEM_COLLISION_SLIGHT, "轻微碰撞" }, 
            { SYSTEM_COLLISION_MEDIUM, "普通碰撞" }, 
            { SYSTEM_COLLISION_SEVERE, "剧烈碰撞" },
            { SYSTEM_COUNTDOWN_5, "倒计时5" },
            { SYSTEM_COUNTDOWN_4, "倒计时4" },
            { SYSTEM_COUNTDOWN_3, "倒计时3" },
            { SYSTEM_COUNTDOWN_2, "倒计时2" },
            { SYSTEM_COUNTDOWN_1, "倒计时1" },
        };

        public static List<string> SYSTEM_COUNTDOWNS = new List<string>()
        {
            SYSTEM_COUNTDOWN_1,
            SYSTEM_COUNTDOWN_2,
            SYSTEM_COUNTDOWN_3,
            SYSTEM_COUNTDOWN_4,
            SYSTEM_COUNTDOWN_5,
        };

        public static List<string> SYSTEM_COLLISION = new List<string>()
        {
            Constants.SYSTEM_COLLISION_SLIGHT, Constants.SYSTEM_COLLISION_MEDIUM, Constants.SYSTEM_COLLISION_SEVERE
        };
        public static List<string> SYSTEM_PUNCTURE = new List<string>{ 
            Constants.SYSTEM_PUNCTURE_FRONT_LEFT, Constants.SYSTEM_PUNCTURE_FRONT_RIGHT,
            Constants.SYSTEM_PUNCTURE_REAR_LEFT, Constants.SYSTEM_PUNCTURE_REAR_RIGHT
        };

        public static IList<string> SupportedAudioTypes { set; get; } = Config.Instance.SupportedAudioTypes;
    }
}
