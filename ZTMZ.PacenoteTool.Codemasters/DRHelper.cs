using System;
using System.Collections.Generic;
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

    public class DRHelper
    {
        // not lazy, initialized when loading the assembly
        private static DRHelper _instance = new DRHelper();
        public static DRHelper Instance => _instance;
        public Dictionary<string, List<ItineraryProperty>> ItineraryMap_DR1 { set; get; } = new();
        
        public Dictionary<string, List<ItineraryProperty>> ItineraryMap_DR2 { set; get; } = new();
        public DRHelper()
        {
            // load dict from json
            var jsonContent = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "track_dict_dr1.json");
            this.ItineraryMap_DR1 = JsonConvert.DeserializeObject<Dictionary<string, List<ItineraryProperty>>>(jsonContent);
            var jsonContent2 = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "track_dict_dr2.json");
            this.ItineraryMap_DR2 = JsonConvert.DeserializeObject<Dictionary<string, List<ItineraryProperty>>>(jsonContent);
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
