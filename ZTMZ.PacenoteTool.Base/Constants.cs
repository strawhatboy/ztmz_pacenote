using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base
{

    public class Constants
    {

        public static string DEFAULT_PROFILE = "default";
        public static string DEFAULT_CODRIVER = "codrivers\\default";
        public static string CODRIVER_FILENAME = "codriver.txt";
        public static string CODRIVER_PACKAGE_INFO_FILENAME = "info.json";
        public static string PATH_GAMES = "games";

        // system sound
        public const string SYSTEM_START_STAGE = "system_start_stage";
        public const string SYSTEM_END_STAGE = "system_end_stage";
        public const string SYSTEM_GO = "system_go";
        public const string SYSTEM_PUNCTURE_FRONT_LEFT = "system_puncture_front_left";
        public const string SYSTEM_PUNCTURE_FRONT_RIGHT = "system_puncture_front_right";
        public const string SYSTEM_PUNCTURE_REAR_LEFT = "system_puncture_rear_left";
        public const string SYSTEM_PUNCTURE_REAR_RIGHT = "system_puncture_rear_right";

        public const string SYSTEM_COLLISION_SLIGHT = "system_collision_slight";
        public const string SYSTEM_COLLISION_MEDIUM = "system_collision_medium";
        public const string SYSTEM_COLLISION_SEVERE = "system_collision_severe";

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
            { SYSTEM_COLLISION_SEVERE, "剧烈碰撞" }
        };

        public static List<string> SYSTEM_COLLISION = new List<string>()
        {
            Constants.SYSTEM_COLLISION_SLIGHT, Constants.SYSTEM_COLLISION_MEDIUM, Constants.SYSTEM_COLLISION_SEVERE
        };
        public static List<string> SYSTEM_PUNCTURE = new List<string>{ 
            Constants.SYSTEM_PUNCTURE_FRONT_LEFT, Constants.SYSTEM_PUNCTURE_FRONT_RIGHT,
            Constants.SYSTEM_PUNCTURE_REAR_LEFT, Constants.SYSTEM_PUNCTURE_REAR_RIGHT
        };
    }
}
