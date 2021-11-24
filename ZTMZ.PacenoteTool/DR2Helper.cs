using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ZTMZ.PacenoteTool
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

    }
    public class ItineraryProperty
    {
        public float start_z { set; get; }
        public string track_name { set; get; }
    }

    public class DR2Helper
    {
        public Dictionary<string, List<ItineraryProperty>> ItineraryMap { set; get; }
        public DR2Helper()
        {
            // load dict from json
            var jsonContent = File.ReadAllText("track_dict.json");
            this.ItineraryMap = JsonConvert.DeserializeObject<Dictionary<string, List<ItineraryProperty>>>(jsonContent);
        }
        public string GetItinerary(string trackLength, float startZ)
        {
            if (this.ItineraryMap.ContainsKey(trackLength))
            {
                var candidates = this.ItineraryMap[trackLength];
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