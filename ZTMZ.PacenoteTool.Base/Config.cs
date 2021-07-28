using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ZTMZ.PacenoteTool.Base
{
    public class Config
    {
        private Config()
        {
            
        }
        private static Config _instance;
        public IList<string> SupportedAudioTypes { set; get; } = new List<string>()
        {
            "*.wav", "*.mp3", "*.aiff", "*.wma", "*.aac", "*.mp4", "*.m4a"
        };

        public float ScriptMode_MinDistanceForMerge { set; get; } = 29.7f;
        public float ScriptMode_PlaySecondsAdvanced { set; get; } = 4f;

        public int UDPListenPort { set; get; } = 20777;

        public void Save()
        {
            File.WriteAllText("config.json", JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static Config Load()
        {
            if (File.Exists("config.json"))
            {
                return JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
            }

            var config = new Config();
            config.Save();
            return config;
        }

        public static Config Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Config.Load();
                }

                return _instance;
            }
        }
    }
    
    
}