using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
namespace ZTMZ.PacenoteTool.Codemasters;

public class WRCVehicle
{
    public int id { get; set; }

    [JsonProperty("class")]
    public int Class { get; set; }
    public int manufacturer { get; set; }
    public string name { get; set; }
    public bool builder { get; set; }
}
public class WRCHelper
{
    // not lazy, initialized when loading the assembly
    private static WRCHelper _instance = new WRCHelper();
    public static WRCHelper Instance => _instance;
    public Dictionary<int, string> ItineraryMap { set; get; } = new();
    public Dictionary<int, string> VehicleManufacturers { set; get; } = new();
    public Dictionary<int, string> VehicleClasses { set; get; } = new();
    public Dictionary<int, string> Locations { set; get; } = new();
    public Dictionary<int, WRCVehicle> Vehicles { set; get; } = new();
    private WRCHelper() {
        var jsonContent = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "WRC_ids.json");
        var jsonObj = JObject.Parse(jsonContent);
        var itineraries = jsonObj["routes"];
        foreach (var item in itineraries) {
            ItineraryMap.Add(item["id"].Value<int>(), item["name"].Value<string>());
        }
        var locations = jsonObj["locations"];
        foreach (var item in locations) {
            Locations.Add(item["id"].Value<int>(), item["name"].Value<string>());
        }
        var vehicle_manufacturers = jsonObj["vehicle_manufacturers"];
        foreach (var item in vehicle_manufacturers) {
            VehicleManufacturers.Add(item["id"].Value<int>(), item["name"].Value<string>());
        }
        var vehicle_classes = jsonObj["vehicle_classes"];
        foreach (var item in vehicle_classes) {
            VehicleClasses.Add(item["id"].Value<int>(), item["name"].Value<string>());
        }
        var vehicles = jsonObj["vehicles"];
        foreach (var item in vehicles) {
            var vehicle = item.ToObject<WRCVehicle>();
            Vehicles.Add(vehicle.id, vehicle);
        }
    }

    public string GetItinerary(IGame game, int locationId, int trackId)
    {
        if (Locations.ContainsKey(locationId) && ItineraryMap.ContainsKey(trackId))
        {
            return $"{Locations[locationId]}_{ItineraryMap[trackId]}";
        }
        return "UnknownTrack";
    }

    public string GetCarName(int carId)
    {
        if (Vehicles.ContainsKey(carId))
        {
            return Vehicles[carId].name;
        }
        return "UnknownCar";
    }

    public string GetCarManufacturer(int carId)
    {
        if (Vehicles.ContainsKey(carId) && VehicleManufacturers.ContainsKey(Vehicles[carId].manufacturer))
        {
            return VehicleManufacturers[Vehicles[carId].manufacturer];
        }
        return "UnknownManufacturer";
    }

    public string GetCarClass(int carId)
    {
        if (Vehicles.ContainsKey(carId) && VehicleClasses.ContainsKey(Vehicles[carId].Class))
        {
            return VehicleClasses[Vehicles[carId].Class];
        }
        return "UnknownClass";
    }
}
