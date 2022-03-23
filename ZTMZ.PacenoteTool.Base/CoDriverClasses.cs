using System.Collections.Generic;
using Newtonsoft.Json;

namespace ZTMZ.PacenoteTool.Base
{

    public class AudioFile
    {
        //public string Extension { set; get; }
        //public string FileName { set; get; }
        //public string FilePath { set; get; }
        public int Distance { set; get; }

        //public AudioFileReader AudioFileReader { set; get; }
        //public byte[] Content { set; get; }
        public AutoResampledCachedSound Sound { set; get; } = null;
    }

    public class CoDriverPackageInfo
    {
        public string name { set; get; }
        public string description { set; get; }
        public string gender { set; get; }
        public string language { set; get; }
        public string homepage { set; get; }
        public string version { set; get; }

        [JsonIgnore] public string Path { set; get; }

        [JsonIgnore]
        public string DisplayText =>
            string.Format("[{0}][{1}] {2}", language, GenderStr, name);

        [JsonIgnore]
        public string GenderStr
        {
            get
            {
                switch (gender)
                {
                    case "M":
                    {
                        return I18NLoader.Instance["misc.gender_male"];
                    }
                    case "F":
                    {
                        return I18NLoader.Instance["misc.gender_female"];
                    }
                }

                return I18NLoader.Instance["misc.gender_unknown"];
            }
        }
    }

    public class CoDriverPackage
    {
        public CoDriverPackageInfo Info { set; get; }
        public Dictionary<string, List<string>> tokensPath { private set; get; } = new();

        public Dictionary<string, List<AutoResampledCachedSound>> tokens { private set; get; } = new();
    }
}