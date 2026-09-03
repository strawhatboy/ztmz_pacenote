using System;
using System.Collections.Generic;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base.Game;

public interface IGameConfig
{
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

    public static string Name => "udp";

    public UdpGameConfig()
    {
    }
}

[GameConfig("memory")]
public class MemoryGameConfig : IGameConfig
{
    public float RefreshRate { set; get; }

    public static string Name => "memory";

    public MemoryGameConfig()
    {
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

    public CommonGameConfigs()
    {
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

    // Values come from the overriding side; labels, types, and ranges come from
    // the current application schema. Rebuilding all parallel lists keeps stale
    // user configurations aligned when settings and their metadata evolve.
    public bool Merge(CommonGameConfigs other, bool isOverride = true)
    {
        ArgumentNullException.ThrowIfNull(other);

        var currentNames = PropertyName ?? new Dictionary<string, string>();
        var currentValues = PropertyValue ?? new List<object>();
        var currentRanges = ValueRange ?? new List<List<double>>();
        var currentTypes = PropertyType ?? new List<string>();
        var incomingNames = other.PropertyName ?? new Dictionary<string, string>();
        var incomingValues = other.PropertyValue ?? new List<object>();
        var incomingRanges = other.ValueRange ?? new List<List<double>>();
        var incomingTypes = other.PropertyType ?? new List<string>();
        var incomingIndexes = incomingNames.Keys
            .Select((key, index) => new { key, index })
            .ToDictionary(item => item.key, item => item.index, StringComparer.Ordinal);

        var mergedNames = new Dictionary<string, string>();
        var mergedValues = new List<object>();
        var mergedRanges = new List<List<double>>();
        var mergedTypes = new List<string>();

        var currentIndex = 0;
        foreach (var current in currentNames)
        {
            var hasIncoming = incomingIndexes.TryGetValue(current.Key, out var incomingIndex);
            var schemaName = hasIncoming && !isOverride
                ? incomingNames.ElementAt(incomingIndex).Value
                : current.Value;
            mergedNames.Add(current.Key, schemaName ?? current.Key);

            var hasCurrentValue = currentIndex < currentValues.Count;
            var hasIncomingValue = hasIncoming && incomingIndex < incomingValues.Count;
            if (isOverride && hasIncomingValue)
            {
                mergedValues.Add(incomingValues[incomingIndex]);
            }
            else if (hasCurrentValue)
            {
                mergedValues.Add(currentValues[currentIndex]);
            }
            else if (hasIncomingValue)
            {
                mergedValues.Add(incomingValues[incomingIndex]);
            }
            else
            {
                mergedValues.Add(null);
            }

            var currentType = currentIndex < currentTypes.Count
                ? currentTypes[currentIndex] ?? string.Empty
                : string.Empty;
            var incomingType = hasIncoming && incomingIndex < incomingTypes.Count
                ? incomingTypes[incomingIndex] ?? string.Empty
                : string.Empty;
            mergedTypes.Add(isOverride
                ? FirstNonEmpty(currentType, incomingType)
                : FirstNonEmpty(incomingType, currentType));

            var currentRange = currentIndex < currentRanges.Count ? currentRanges[currentIndex] : null;
            var incomingRange = hasIncoming && incomingIndex < incomingRanges.Count
                ? incomingRanges[incomingIndex]
                : null;
            mergedRanges.Add((isOverride ? currentRange ?? incomingRange : incomingRange ?? currentRange)?.ToList()
                ?? new List<double>());
            currentIndex++;
        }

        var existingKeys = new HashSet<string>(currentNames.Keys, StringComparer.Ordinal);
        var nextIncomingIndex = 0;
        foreach (var incoming in incomingNames)
        {
            if (!existingKeys.Contains(incoming.Key))
            {
                mergedNames.Add(incoming.Key, incoming.Value ?? incoming.Key);
                mergedValues.Add(nextIncomingIndex < incomingValues.Count
                    ? incomingValues[nextIncomingIndex]
                    : null);
                mergedTypes.Add(nextIncomingIndex < incomingTypes.Count
                    ? incomingTypes[nextIncomingIndex] ?? string.Empty
                    : string.Empty);
                mergedRanges.Add(nextIncomingIndex < incomingRanges.Count
                    ? incomingRanges[nextIncomingIndex]?.ToList() ?? new List<double>()
                    : new List<double>());
            }

            nextIncomingIndex++;
        }

        var changed = !DictionaryEquals(currentNames, mergedNames)
            || !currentValues.SequenceEqual(mergedValues)
            || !currentTypes.SequenceEqual(mergedTypes, StringComparer.Ordinal)
            || !RangesEqual(currentRanges, mergedRanges);

        PropertyName = mergedNames;
        PropertyValue = mergedValues;
        PropertyType = mergedTypes;
        ValueRange = mergedRanges;
        return changed;
    }

    private static string FirstNonEmpty(string preferred, string fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    private static bool RangesEqual(
        IReadOnlyList<List<double>> left,
        IReadOnlyList<List<double>> right) =>
        left.Count == right.Count && left
            .Select((range, index) => (range, index))
            .All(item => (item.range ?? new List<double>()).SequenceEqual(right[item.index]));
}
