using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
namespace ZTMZ.PacenoteTool.Codemasters;

public class WRCItineraryProperty
{
    public float start_z { set; get; }
    public string track_name { set; get; } = "";
    public float end_track_length { set; get; }
}
public class WRCHelper
{
    // not lazy, initialized when loading the assembly
    private static WRCHelper _instance = new WRCHelper();
    public static WRCHelper Instance => _instance;
    public Dictionary<int, string> ItineraryMap { set; get; } = new();
    private WRCHelper() {
        var jsonContent = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "WRC_ids.json");
        var jsonObj = JObject.Parse(jsonContent);
        var itineraries = jsonObj["routes"];
        var locations = jsonObj["locations"];
        var vehicle_manufacturers = jsonObj["vehicle_manufacturers"];
        var vehicle_classes = jsonObj["vehicle_classes"];
        var vehicles = jsonObj["vehicles"];
    }

    public string GetItinerary(IGame game, string trackLength, float startZ)
    {
        return GetItinerary(this.ItineraryMap, trackLength, startZ);
    }

    // copy paste, ugly
    public string GetItinerary(Dictionary<string, List<WRCItineraryProperty>> itineraryMap, string trackLength, float startZ) {
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
