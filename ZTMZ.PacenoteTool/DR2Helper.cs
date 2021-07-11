using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ZTMZ.PacenoteTool
{
    
    public class DR2Helper
    {
        public Dictionary<string, List<Tuple<float, string>>> ItineraryMap { set; get; }
        public DR2Helper()
        {
            // load dict from json
            var jsonContent = File.ReadAllText("track_dict.json");
            this.ItineraryMap = JsonConvert.DeserializeObject<Dictionary<string, List<Tuple<float, string>>>>(jsonContent);
        }
        public string GetItinerary(string trackLength, float startZ)
        {
            var candidates = this.ItineraryMap[trackLength];
            float min = float.MaxValue;
            int minIndex = 0;
            for(int i = 0; i < candidates.Count; i++)
            {
                var item = candidates[i];
                var diff = Math.Abs(item.Item1 - startZ); 
                if (diff < min)
                {
                    min = diff;
                    minIndex = i;
                }
            }

            return candidates[minIndex].Item2.Replace(',', '_');
        }
    }
}