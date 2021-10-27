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
        public IList<string> SupportedAudioTypes { set; get; }

        public float ScriptMode_MinDistanceForMerge { set; get; } = 29.7f;
        public float ScriptMode_PlaySecondsAdvanced { set; get; } = 4f;

        public bool UseSequentialMixerToHandleAudioConflict { set; get; } = true;

        public int UDPListenPort { set; get; } = 20777;

        public bool WarnIfPortMismatch { set; get; } = true;

        public int PlaybackDeviceDesiredLatency = 175;

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
            config.SupportedAudioTypes = new List<string>()
            {
                "*.wav", "*.mp3", "*.aiff", "*.wma", "*.aac", "*.mp4", "*.m4a"
            };
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