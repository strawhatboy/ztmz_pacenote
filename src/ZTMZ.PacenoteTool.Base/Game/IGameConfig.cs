using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;

namespace ZTMZ.PacenoteTool.Base.Game;

public interface IGameConfig
{
    // IGameConfigSettingsPane UI { get; set; }
}


public class GameConfigAttribute : Attribute
{
    public string name;

    public GameConfigAttribute(string name) 
    {
        this.name = name;
    }
}

[GameConfigAttribute("udp")]
public class UdpGameConfig : IGameConfig
{
    public string IPAddress { set; get; }
    public int Port { set; get; }

    [JsonIgnore]
    // public IGameConfigSettingsPane UI { set; get; }

    public static string Name => "udp";

    public UdpGameConfig()
    {
        // UI = new UdpGameConfigSettingsPane(this);
    }
}

[GameConfig("memory")]
public class MemoryGameConfig : IGameConfig
{
    public float RefreshRate { set; get; }

    public static string Name => "memory";

    // [JsonIgnore]
    // public IGameConfigSettingsPane UI { set; get; }

    public MemoryGameConfig()
    {
        // UI = new MemoryGameConfigSettingsPane(this);
    }
}

[GameConfig("common_config")]
public class CommonGameConfigs: IGameConfig
{
    public static string Name => "common_config";

    public Dictionary<string, string> PropertyName { set; get; } = new();
    public List<object> PropertyValue { set; get; } = new();

    public List<List<double>> ValueRange { set; get; } = new();

    public List<string> PropertyType { set; get; } = new();

    // [JsonIgnore]
    // public IGameConfigSettingsPane UI {set;get;}

    public CommonGameConfigs()
    {
        // UI = new CommonGameConfigsSettingsPane(this);
    }

    public object this[string idx]
    {
        get {
            var index = 0;
            foreach (var kv in PropertyName)
            {
                if (kv.Key == idx)
                {
                    return PropertyValue[index];
                }
                index++;
            }
            return null;
        }
        set {
            var index = 0;
            foreach (var kv in PropertyName)
            {
                if (kv.Key == idx)
                {
                    PropertyValue[index] = value;
                    return;
                }
                index++;
            }
        }
    }

    // merge from another common config
    public void Merge(CommonGameConfigs other, bool isOverride = true)
    {
        for(int i = 0; i < other.PropertyName.Count; i++)
        {
            var kv = other.PropertyName.ElementAt(i);
            if (!PropertyName.ContainsKey(kv.Key))
            {
                PropertyName.Add(kv.Key, kv.Value);
                PropertyValue.Add(other.PropertyValue[i]);
                if (other.PropertyType.Count > i) {
                    PropertyType.Add(other.PropertyType[i]);
                }
                if (other.ValueRange.Count > i) {
                    ValueRange.Add(other.ValueRange[i]);
                }
            } else {
                if (isOverride) {
                    var index = PropertyName.Keys.ToList().IndexOf(kv.Key);
                    PropertyValue[index] = other.PropertyValue[i];
                    if (other.PropertyType.Count > i) {
                        if (PropertyType.Count <= index) {
                            PropertyType.Add(other.PropertyType[i]);
                        } else {
                            PropertyType[index] = other.PropertyType[i];
                        }
                    }
                }
                // DO NOT OVERRIDE THE VALUE_RANGE ValueRange[index] = other.ValueRange[i];
            }
        }
    }
}

