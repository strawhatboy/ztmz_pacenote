using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters
{
    public class GameHacker
    {
        public static long ADDR_SOUND_MENU_1 = 0x022ABE2A4F78;
        public static long ADDR_SOUND_MENU_2 = 0x022ABE370B48;

        public static long ADDR_SOUND_REPLAY_1 = 0x022ABE2A5718;
        public static long ADDR_SOUND_REPLAY_2 = 0x022ABE370B4C;


        public static long ADDR_SOUND_AMBIENT_1 = 0x022A62B2D030;
        public static long ADDR_SOUND_AMBIENT_2 = 0x022ABE370B50;   // real

        public static long ADDR_SOUND_ENGINE_1 = 0x022A62B2CF70;
        public static long ADDR_SOUND_ENGINE_2 = 0x022ABE370B54;    // real

        public static long ADDR_SOUND_SPEECH_1 = 0x022ABE2A6DF8;
        public static long ADDR_SOUND_SPEECH_2 = 0x022ABE370B28;
        public static long ADDR_SOUND_SPEECH_3 = 0x022ABE370B58;

        public static long ADDR_SOUND_SURFACES_1 = 0x022A62B2BFB0;
        public static long ADDR_SOUND_SURFACES_2 = 0x022ABE370B5C;  // real

        public static long ADDR_SOUND_VOICE_CHAT_1 = 0x022ABE2A7D38;
        public static long ADDR_SOUND_VOICE_CHAT_2 = 0x022ABE370B60;

        public static string DLL_X_AUDIO_2_7 = "XAudio2_7.dll";
        public static string DLL_X_AUDIO_2_8 = "XAudio2_8.dll";
        public static string DLL_X_AUDIO_2_9 = "XAudio2_9.dll";

        public static void HackDLLs(string gamePath)
        {
            if (!File.Exists(Path.Join(gamePath, DLL_X_AUDIO_2_7)))
                File.Copy(DLL_X_AUDIO_2_7, Path.Join(gamePath, DLL_X_AUDIO_2_7));
            if (!File.Exists(Path.Join(gamePath, DLL_X_AUDIO_2_8)))
                File.Copy(DLL_X_AUDIO_2_8, Path.Join(gamePath, DLL_X_AUDIO_2_8));
            if (!File.Exists(Path.Join(gamePath, DLL_X_AUDIO_2_9)))
                File.Copy(DLL_X_AUDIO_2_9, Path.Join(gamePath, DLL_X_AUDIO_2_9));
        }
        public static void UnhackDLLs(string gamePath)
        {
            File.Delete(Path.Join(gamePath, DLL_X_AUDIO_2_7));
            File.Delete(Path.Join(gamePath, DLL_X_AUDIO_2_8));
            File.Delete(Path.Join(gamePath, DLL_X_AUDIO_2_9));
        }
    }
    public class ItineraryProperty
    {
        public float start_z { set; get; }
        public string track_name { set; get; } = "";
    }

    public class CarNameAndShiftRPMPercentage
    {
        public string car_name { set; get; } = "";
        public float shift_rpm_percentage { set; get; } = 0.0f;
        public string car_class { set; get; } = "";
    }

    public class DRHelper
    {
        // not lazy, initialized when loading the assembly
        private static DRHelper _instance = new DRHelper();
        public static DRHelper Instance => _instance;
        public Dictionary<string, List<ItineraryProperty>> ItineraryMap_DR1 { set; get; } = new();
        
        public Dictionary<string, List<ItineraryProperty>> ItineraryMap_DR2 { set; get; } = new();

        public Dictionary<string, CarNameAndShiftRPMPercentage> CarMap_DR1 { set; get; } = new();
        public Dictionary<string, CarNameAndShiftRPMPercentage> CarMap_DR2 { set; get; } = new();
        private DRHelper()
        {
            // load dict from json
            // File.ReadAllText(new Uri("pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/track_dict_dr1.json").LocalPath);
            var jsonContent = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "track_dict_dr1.json");
            this.ItineraryMap_DR1 = JsonConvert.DeserializeObject<Dictionary<string, List<ItineraryProperty>>>(jsonContent);
            // File.ReadAllText(new Uri("pack://application:,,,/ZTMZ.PacenoteTool.Codemasters;component/track_dict_dr2.json").LocalPath);
            var jsonContent2 = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "track_dict_dr2.json");
            this.ItineraryMap_DR2 = JsonConvert.DeserializeObject<Dictionary<string, List<ItineraryProperty>>>(jsonContent);

            var jsonContent3 = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "car_dict_dr1.json");
            this.CarMap_DR1 = JsonConvert.DeserializeObject<Dictionary<string, CarNameAndShiftRPMPercentage>>(jsonContent3);
            var jsonContent4 = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "car_dict_dr2.json");
            this.CarMap_DR2 = JsonConvert.DeserializeObject<Dictionary<string, CarNameAndShiftRPMPercentage>>(jsonContent4);
        }

        public string GetCarName(IGame game, float idleRPM, float maxRPM, float maxGears) {
            Dictionary<string, CarNameAndShiftRPMPercentage> carMap;
            if (game is DirtRally) 
            {
                carMap = this.CarMap_DR1;
            } else if (game is DirtRally2) {
                carMap = this.CarMap_DR2;
            } else {
                return "UnknownCar";
            }
            return GetCarName(carMap, idleRPM, maxRPM, maxGears);
        }

        public string GetCarName(Dictionary<string, CarNameAndShiftRPMPercentage> carMap, float idleRPM, float maxRPM, float maxGears) {
            var key = $"{(maxRPM/10).ToString("f2", CultureInfo.InvariantCulture)}_{(idleRPM/10).ToString("f2", CultureInfo.InvariantCulture)}_{maxGears.ToString("f2", CultureInfo.InvariantCulture)}";
            if (carMap.ContainsKey(key))
            {
                return carMap[key].car_name;
            }
            return "UnknownCar";
        }

        public float GetCarShiftPercentage(IGame game, float idleRPM, float maxRPM, float maxGears) {
            Dictionary<string, CarNameAndShiftRPMPercentage> carMap;
            if (game is DirtRally) 
            {
                carMap = this.CarMap_DR1;
            } else if (game is DirtRally2) {
                carMap = this.CarMap_DR2;
            } else {
                return 0.9f;
            }
            return GetCarShiftPercentage(carMap, idleRPM, maxRPM, maxGears);
        }

        public float GetCarShiftPercentage(Dictionary<string, CarNameAndShiftRPMPercentage> carMap, float idleRPM, float maxRPM, float maxGears) {
            var key = $"{(maxRPM/10).ToString("f2", CultureInfo.InvariantCulture)}_{(idleRPM/10).ToString("f2", CultureInfo.InvariantCulture)}_{maxGears.ToString("f2", CultureInfo.InvariantCulture)}";
            if (carMap.ContainsKey(key))
            {
                return carMap[key].shift_rpm_percentage;
            }
            return 0.9f;
        }

        public string GetCarClass(IGame game, float idleRPM, float maxRPM, float maxGears) {
            Dictionary<string, CarNameAndShiftRPMPercentage> carMap;
            if (game is DirtRally) 
            {
                carMap = this.CarMap_DR1;
            } else if (game is DirtRally2) {
                carMap = this.CarMap_DR2;
            } else {
                return "UnknownCarClass";
            }
            return GetCarClass(carMap, idleRPM, maxRPM, maxGears);
        }

        public string GetCarClass(Dictionary<string, CarNameAndShiftRPMPercentage> carMap, float idleRPM, float maxRPM, float maxGears) {
            var key = $"{(maxRPM/10).ToString("f2", CultureInfo.InvariantCulture)}_{(idleRPM/10).ToString("f2", CultureInfo.InvariantCulture)}_{maxGears.ToString("f2", CultureInfo.InvariantCulture)}";
            if (carMap.ContainsKey(key))
            {
                return carMap[key].car_class;
            }
            return "UnknownCarClass";
        }

        public string GetItinerary(IGame game, string trackLength, float startZ)
        {
            Dictionary<string, List<ItineraryProperty>> itineraryMap;
            if (game is DirtRally) 
            {
                itineraryMap = this.ItineraryMap_DR1;
            } else if (game is DirtRally2) {
                itineraryMap = this.ItineraryMap_DR2;
            } else {
                return "UnknownTrack";
            }
            return GetItinerary(itineraryMap, trackLength, startZ);
        }

        public string GetItinerary(Dictionary<string, List<ItineraryProperty>> itineraryMap, string trackLength, float startZ) {
            if (itineraryMap.ContainsKey(trackLength))
            {
                var candidates = itineraryMap[trackLength];
                float min = float.MaxValue;
                int minIndex = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    var item = candidates[i];
                    var diff = Math.Abs(item.start_z - startZ);
                    if (diff < min)
                    {
                        min = diff;
                        minIndex = i;
                    }
                }

                return candidates[minIndex].track_name.Replace(',', '_');
            }
            return "UnknownTrack";
        }
    }
}
