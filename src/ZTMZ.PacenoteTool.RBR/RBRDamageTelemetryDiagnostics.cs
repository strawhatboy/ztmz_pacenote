using System;
using System.Collections.Generic;
using System.Linq;

namespace ZTMZ.PacenoteTool.RBR;

public sealed record RBRDamageTelemetryDiagnostics(
    string Status,
    string LastError,
    long SnapshotCount,
    DateTimeOffset? CapturedAtUtc,
    DateTimeOffset? LastChangeUtc,
    IReadOnlyList<string> LastChangedComponents,
    RBRDamageTelemetrySnapshot? LatestSnapshot);

/// <summary>
/// A read-only snapshot of the RBR damage block. Parts retain their raw values;
/// groups mirror the seven RBRHUD dashboard indicators.
/// </summary>
public sealed record RBRDamageTelemetrySnapshot(
    DateTimeOffset CapturedAtUtc,
    int PhysicsAddress,
    int DamageAddress,
    IReadOnlyList<RBRDamagePartTelemetry> Parts,
    IReadOnlyList<RBRDamageGroupTelemetry> Groups)
{
    public string ToLogText()
    {
        var groups = Groups
            .Where(group => group.HudLevel is > 1)
            .Select(group =>
                $"{group.Name}={group.Severity}({string.Join(",", group.AffectedParts)})")
            .ToArray();
        var unavailableGroups = Groups
            .Where(group => !group.IsComplete)
            .Select(group => $"{group.Name}=partial")
            .ToArray();
        var summary = groups.Concat(unavailableGroups).DefaultIfEmpty("none");

        return string.Join(
            "; ",
            $"physics={RBRDamageTelemetryFormatter.Address(PhysicsAddress)}",
            $"damage={RBRDamageTelemetryFormatter.Address(DamageAddress)}",
            $"groups={string.Join(",", summary)}");
    }
}

/// <summary>
/// Raw data for one RBRHUD-compatible damage telemetry item. HudLevel uses the
/// observed RBRHUD range: 1 is undamaged and 5 is its most severe display state.
/// </summary>
public sealed record RBRDamagePartTelemetry(
    string Name,
    int Address,
    bool IsReadable,
    float? FloatValue,
    int? IntegerValue,
    IReadOnlyList<float> FloatValues,
    IReadOnlyList<int> IntegerValues,
    int? HudLevel)
{
    public int? Severity => HudLevel.HasValue ? HudLevel.Value - 1 : null;
}

/// <summary>
/// A compact RBRHUD-compatible dashboard group. A partial group must not be
/// treated as complete because one or more constituent raw values were unreadable.
/// </summary>
public sealed record RBRDamageGroupTelemetry(
    string Name,
    IReadOnlyList<string> PartNames,
    bool IsComplete,
    int? HudLevel,
    IReadOnlyList<string> AffectedParts)
{
    public int? Severity => HudLevel.HasValue ? HudLevel.Value - 1 : null;
}

internal enum RBRDamageReadKind
{
    Float,
    Integer,
    FloatAndInteger,
    FloatArray,
    IntegerArray,
    Pointer
}

internal enum RBRDamageConverter
{
    None,
    IntegerZeroCritical,
    IntegerZeroSevere,
    IntegerNonZeroCritical,
    IntegerEnum,
    FloatD230,
    FloatD290,
    FloatD320,
    FloatD370,
    FloatD3D0,
    FloatD430,
    FloatD490,
    FloatDC70,
    FloatArrayD740,
    FloatArrayD870,
    FloatAndIntegerD950,
    FloatArrayDAE0,
    FloatArrayDCF0,
    IntegerArrayD0F0
}

internal sealed record RBRDamagePartDefinition(
    string Name,
    int Offset,
    RBRDamageReadKind ReadKind,
    RBRDamageConverter Converter,
    int ValueCount = 1);

internal sealed record RBRDamageGroupDefinition(
    string Name,
    IReadOnlyList<string> PartNames);

internal static class RBRDamageTelemetryCatalog
{
    // Names and converter behavior are derived from the analyzed RBRHUD build.
    // The raw data remains available even where a converter is not yet known.
    public static IReadOnlyList<RBRDamagePartDefinition> PartDefinitions { get; } = new[]
    {
        new RBRDamagePartDefinition("FrontDiff", 0x000, RBRDamageReadKind.FloatAndInteger, RBRDamageConverter.FloatAndIntegerD950),
        new RBRDamagePartDefinition("CenterDiff", 0x008, RBRDamageReadKind.FloatAndInteger, RBRDamageConverter.FloatAndIntegerD950),
        new RBRDamagePartDefinition("RearDiff", 0x010, RBRDamageReadKind.FloatAndInteger, RBRDamageConverter.FloatAndIntegerD950),
        new RBRDamagePartDefinition("DriveShaftLF", 0x018, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("BrakeCaliperLF", 0x01C, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("DriveShaftRF", 0x024, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("BrakeCaliperRF", 0x028, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("DriveShaftLR", 0x030, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("BrakeCaliperLR", 0x034, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("DriveShaftRR", 0x03C, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("BrakeCaliperRR", 0x040, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("Gearbox", 0x048, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayDAE0, 17),
        new RBRDamagePartDefinition("GearActuator", 0x078, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("Starter", 0x07C, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("BrakeCircuit", 0x080, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("Clutch", 0x084, RBRDamageReadKind.Float, RBRDamageConverter.FloatD230),
        new RBRDamagePartDefinition("Battery", 0x08C, RBRDamageReadKind.Float, RBRDamageConverter.FloatD290),
        new RBRDamagePartDefinition("Hydraulics", 0x090, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("HeadGasket", 0x098, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayD870, 14),
        new RBRDamagePartDefinition("WaterPump", 0x0DC, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("Radiator", 0x0E8, RBRDamageReadKind.Float, RBRDamageConverter.FloatD320),
        new RBRDamagePartDefinition("OilPump", 0x0F0, RBRDamageReadKind.Float, RBRDamageConverter.FloatD370),
        new RBRDamagePartDefinition("OilCooler", 0x0F4, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerNonZeroCritical),
        new RBRDamagePartDefinition("Intercooler", 0x0F8, RBRDamageReadKind.Float, RBRDamageConverter.FloatDC70),
        new RBRDamagePartDefinition("DamperMountLF", 0x1EC, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayDCF0, 2),
        new RBRDamagePartDefinition("SteeringRodLF", 0x1F4, RBRDamageReadKind.Float, RBRDamageConverter.FloatD490),
        new RBRDamagePartDefinition("ControlArmLF", 0x1F8, RBRDamageReadKind.Float, RBRDamageConverter.FloatD490),
        new RBRDamagePartDefinition("HubLF", 0x1FC, RBRDamageReadKind.Float, RBRDamageConverter.FloatD3D0),
        new RBRDamagePartDefinition("WheelLF", 0x200, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayD740, 32),
        new RBRDamagePartDefinition("Electrical", 0x1E8, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroSevere),
        new RBRDamagePartDefinition("DamperMountRF", 0x224, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayDCF0, 2),
        new RBRDamagePartDefinition("SteeringRodRF", 0x22C, RBRDamageReadKind.Float, RBRDamageConverter.FloatD490),
        new RBRDamagePartDefinition("ControlArmRF", 0x230, RBRDamageReadKind.Float, RBRDamageConverter.FloatD490),
        new RBRDamagePartDefinition("HubRF", 0x234, RBRDamageReadKind.Float, RBRDamageConverter.FloatD3D0),
        new RBRDamagePartDefinition("WheelRF", 0x238, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayD740, 32),
        new RBRDamagePartDefinition("DamperMountLR", 0x25C, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayDCF0, 2),
        new RBRDamagePartDefinition("SuspensionArmLR", 0x264, RBRDamageReadKind.Float, RBRDamageConverter.FloatD490),
        new RBRDamagePartDefinition("ControlArmLR", 0x268, RBRDamageReadKind.Float, RBRDamageConverter.FloatD490),
        new RBRDamagePartDefinition("HubLR", 0x26C, RBRDamageReadKind.Float, RBRDamageConverter.FloatD3D0),
        new RBRDamagePartDefinition("WheelLR", 0x270, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayD740, 32),
        new RBRDamagePartDefinition("DamperMountRR", 0x294, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayDCF0, 2),
        new RBRDamagePartDefinition("SuspensionArmRR", 0x29C, RBRDamageReadKind.Float, RBRDamageConverter.FloatD490),
        new RBRDamagePartDefinition("ControlArmRR", 0x2A0, RBRDamageReadKind.Float, RBRDamageConverter.FloatD490),
        new RBRDamagePartDefinition("HubRR", 0x2A4, RBRDamageReadKind.Float, RBRDamageConverter.FloatD3D0),
        new RBRDamagePartDefinition("WheelRR", 0x2A8, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayD740, 32),
        new RBRDamagePartDefinition("DamperLF", 0x2D0, RBRDamageReadKind.Float, RBRDamageConverter.FloatD430),
        new RBRDamagePartDefinition("DamperRF", 0x2D8, RBRDamageReadKind.Float, RBRDamageConverter.FloatD430),
        new RBRDamagePartDefinition("DamperLR", 0x2E0, RBRDamageReadKind.Float, RBRDamageConverter.FloatD430),
        new RBRDamagePartDefinition("DamperRR", 0x2E8, RBRDamageReadKind.Float, RBRDamageConverter.FloatD430),
        new RBRDamagePartDefinition("TyreLF", 0x2EC, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayD740, 32),
        new RBRDamagePartDefinition("TyreRF", 0x30C, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayD740, 32),
        new RBRDamagePartDefinition("TyreLR", 0x32C, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayD740, 32),
        new RBRDamagePartDefinition("TyreRR", 0x34C, RBRDamageReadKind.FloatArray, RBRDamageConverter.FloatArrayD740, 32),
        new RBRDamagePartDefinition("LeftDoorLock", 0x36C, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("LeftDoor", 0x370, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("RightDoorLock", 0x378, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("RightDoor", 0x37C, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("BonnetLock", 0x384, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("Bonnet", 0x388, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("Exhaust", 0x394, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("ExhaustMount", 0x398, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerZeroCritical),
        new RBRDamagePartDefinition("FrontBumper", 0x39C, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerEnum),
        new RBRDamagePartDefinition("RearBumper", 0x3A0, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerEnum),
        new RBRDamagePartDefinition("Wing", 0x3A4, RBRDamageReadKind.Integer, RBRDamageConverter.IntegerEnum),
        new RBRDamagePartDefinition("Windshield", 0x3AC, RBRDamageReadKind.IntegerArray, RBRDamageConverter.IntegerArrayD0F0, 9),
        new RBRDamagePartDefinition("Bodywork", 0x488, RBRDamageReadKind.Pointer, RBRDamageConverter.None)
    };

    private static readonly IReadOnlyList<RBRDamageGroupDefinition> GroupDefinitions = new[]
    {
        new RBRDamageGroupDefinition("Radiator", new[] { "Radiator", "WaterPump", "Intercooler" }),
        new RBRDamageGroupDefinition("Engine", new[] { "Battery", "Clutch", "GearActuator", "Gearbox", "HeadGasket", "OilPump", "OilCooler", "Starter" }),
        new RBRDamageGroupDefinition("Diff", new[] { "CenterDiff", "FrontDiff", "RearDiff" }),
        new RBRDamageGroupDefinition("FrontRight", new[] { "BrakeCaliperRF", "ControlArmRF", "DamperMountRF", "DamperRF", "DriveShaftRF", "HubRF", "SteeringRodRF", "WheelRF" }),
        new RBRDamageGroupDefinition("RearRight", new[] { "BrakeCaliperRR", "ControlArmRR", "DamperMountRR", "DamperRR", "DriveShaftRR", "HubRR", "SuspensionArmRR", "WheelRR" }),
        new RBRDamageGroupDefinition("RearLeft", new[] { "BrakeCaliperLR", "ControlArmLR", "DamperLR", "DamperMountLR", "DriveShaftLR", "HubLR", "SuspensionArmLR", "WheelLR" }),
        new RBRDamageGroupDefinition("FrontLeft", new[] { "BrakeCaliperLF", "ControlArmLF", "DamperLF", "DamperMountLF", "DriveShaftLF", "HubLF", "SteeringRodLF", "WheelLF" })
    };

    public static RBRDamagePartTelemetry CreatePart(
        RBRDamagePartDefinition definition,
        int address,
        bool isReadable,
        float? floatValue = null,
        int? integerValue = null,
        IReadOnlyList<float>? floatValues = null,
        IReadOnlyList<int>? integerValues = null)
    {
        var normalizedFloatValues = floatValues ?? Array.Empty<float>();
        var normalizedIntegerValues = integerValues ?? Array.Empty<int>();
        var hudLevel = isReadable
            ? GetHudLevel(definition.Converter, floatValue, integerValue, normalizedFloatValues, normalizedIntegerValues)
            : null;

        return new RBRDamagePartTelemetry(
            definition.Name,
            address,
            isReadable,
            floatValue,
            integerValue,
            normalizedFloatValues,
            normalizedIntegerValues,
            hudLevel);
    }

    public static IReadOnlyList<RBRDamageGroupTelemetry> CreateGroups(IReadOnlyList<RBRDamagePartTelemetry> parts)
    {
        var partsByName = parts.ToDictionary(part => part.Name, StringComparer.Ordinal);
        var groups = new List<RBRDamageGroupTelemetry>(GroupDefinitions.Count);

        foreach (var definition in GroupDefinitions)
        {
            var groupParts = definition.PartNames
                .Where(partsByName.ContainsKey)
                .Select(name => partsByName[name])
                .ToArray();
            var knownLevels = groupParts
                .Where(part => part.HudLevel.HasValue)
                .Select(part => part.HudLevel!.Value)
                .ToArray();
            var affectedParts = groupParts
                .Where(part => part.HudLevel is > 1)
                .Select(part => part.Name)
                .ToArray();
            var isComplete = groupParts.Length == definition.PartNames.Count &&
                             groupParts.All(part => part.IsReadable && part.HudLevel.HasValue);

            groups.Add(new RBRDamageGroupTelemetry(
                definition.Name,
                definition.PartNames,
                isComplete,
                knownLevels.Length == 0 ? null : knownLevels.Max(),
                affectedParts));
        }

        return groups;
    }

    private static int? GetHudLevel(
        RBRDamageConverter converter,
        float? floatValue,
        int? integerValue,
        IReadOnlyList<float> floatValues,
        IReadOnlyList<int> integerValues)
    {
        return converter switch
        {
            RBRDamageConverter.None => null,
            RBRDamageConverter.IntegerZeroCritical => integerValue.HasValue ? integerValue.Value == 0 ? 5 : 1 : null,
            RBRDamageConverter.IntegerZeroSevere => integerValue.HasValue ? integerValue.Value == 0 ? 4 : 1 : null,
            RBRDamageConverter.IntegerNonZeroCritical => integerValue.HasValue ? integerValue.Value != 0 ? 5 : 1 : null,
            RBRDamageConverter.IntegerEnum => integerValue.HasValue ? GetIntegerEnumHudLevel(integerValue.Value) : null,
            RBRDamageConverter.FloatD230 => TryGetFinite(floatValue, out var d230) ? GetFloatD230HudLevel(d230) : null,
            RBRDamageConverter.FloatD290 => TryGetFinite(floatValue, out var d290) ? GetFloatD290HudLevel(d290) : null,
            RBRDamageConverter.FloatD320 => TryGetFinite(floatValue, out var d320) ? GetFloatD320HudLevel(d320) : null,
            RBRDamageConverter.FloatD370 => TryGetFinite(floatValue, out var d370) ? d370 <= 0f ? 5 : 1 : null,
            RBRDamageConverter.FloatD3D0 => TryGetFinite(floatValue, out var d3d0) ? GetFloatD3D0HudLevel(d3d0) : null,
            RBRDamageConverter.FloatD430 => TryGetFinite(floatValue, out var d430) ? GetFloatD430HudLevel(d430) : null,
            RBRDamageConverter.FloatD490 => TryGetFinite(floatValue, out var d490) ? GetFloatD490HudLevel(d490) : null,
            RBRDamageConverter.FloatDC70 => TryGetFinite(floatValue, out var dc70) ? GetFloatDC70HudLevel(dc70) : null,
            RBRDamageConverter.FloatArrayD740 => TryGetInverseAverage(floatValues, 8, out var d740) ? GetFloatD740HudLevel(d740) : null,
            RBRDamageConverter.FloatArrayD870 => TryGetInverseMinimum(floatValues, new[] { 1, 5, 9, 13 }, out var d870) ? GetFloatD870HudLevel(d870) : null,
            RBRDamageConverter.FloatAndIntegerD950 => TryGetFinite(floatValue, out var d950) && integerValue.HasValue ? GetFloatAndIntegerD950HudLevel(d950, integerValue.Value) : null,
            RBRDamageConverter.FloatArrayDAE0 => TryGetMinimum(floatValues, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 16 }, out var dae0) ? GetFloatDAE0HudLevel(dae0) : null,
            RBRDamageConverter.FloatArrayDCF0 => TryGetInverseMinimum(floatValues, new[] { 0, 1 }, out var dcf0) ? GetFloatDCF0HudLevel(dcf0) : null,
            RBRDamageConverter.IntegerArrayD0F0 => GetIntegerArrayD0F0HudLevel(integerValues),
            _ => null
        };
    }

    private static int GetIntegerEnumHudLevel(int value)
    {
        return value switch
        {
            1 => 3,
            2 => 5,
            _ => 1
        };
    }

    private static int GetFloatD230HudLevel(float value)
    {
        if (value <= 0.3f) return 5;
        if (value <= 0.5f) return 4;
        if (value <= 0.7f) return 3;
        return value <= 0.95f ? 2 : 1;
    }

    private static int GetFloatD290HudLevel(float value)
    {
        if (value <= 0.5f) return 5;
        if (value <= 0.65f) return 4;
        if (value <= 0.8f) return 3;
        return value <= 0.9f ? 2 : 1;
    }

    private static int GetFloatD320HudLevel(float value)
    {
        if (value < 0.005f) return 1;
        if (value < 0.03f) return 2;
        if (value < 0.2f) return 3;
        return 5;
    }

    private static int GetFloatD3D0HudLevel(float value)
    {
        var absoluteValue = MathF.Abs(value);
        if (absoluteValue < 0.01f) return 1;
        if (absoluteValue < 0.05f) return 2;
        if (absoluteValue < 0.1f) return 3;
        return 4;
    }

    private static int GetFloatD430HudLevel(float value)
    {
        if (value < 0.02f) return 1;
        if (value < 0.1f) return 2;
        if (value < 0.4f) return 3;
        if (value < 0.7f) return 4;
        return 5;
    }

    private static int GetFloatD490HudLevel(float value)
    {
        if (value < 0.02f) return 1;
        if (value < 0.1f) return 2;
        if (value < 0.3f) return 3;
        return 4;
    }

    private static int GetFloatDC70HudLevel(float value)
    {
        if (value < 0.01f) return 1;
        if (value < 0.05f) return 2;
        if (value < 0.1f) return 3;
        if (value < 0.4f) return 4;
        return 5;
    }

    private static int GetFloatD740HudLevel(float inverseAverage)
    {
        if (inverseAverage <= 0.2f) return 5;
        if (inverseAverage < 0.4f) return 4;
        if (inverseAverage <= 0.6f) return 3;
        return inverseAverage < 0.8f ? 2 : 1;
    }

    private static int GetFloatD870HudLevel(float inverseMinimum)
    {
        if (inverseMinimum <= 0.3f) return 5;
        if (inverseMinimum <= 0.8f) return 4;
        if (inverseMinimum <= 0.95f) return 3;
        return inverseMinimum <= 0.97f ? 2 : 1;
    }

    private static int GetFloatAndIntegerD950HudLevel(float value, int status)
    {
        if (value <= 0.5f) return 4;
        if (value <= 0.7f) return 3;
        if (value <= 0.85f) return 2;
        return status == 0 ? 5 : 1;
    }

    private static int GetFloatDAE0HudLevel(float minimum)
    {
        if (minimum <= 0.2f) return 5;
        if (minimum <= 0.5f) return 4;
        if (minimum <= 0.7f) return 3;
        return minimum <= 0.9f ? 2 : 1;
    }

    private static int GetFloatDCF0HudLevel(float inverseMinimum)
    {
        if (inverseMinimum <= 0.8f) return 4;
        if (inverseMinimum <= 0.95f) return 3;
        return inverseMinimum <= 0.99f ? 2 : 1;
    }

    private static int? GetIntegerArrayD0F0HudLevel(IReadOnlyList<int> values)
    {
        if (values.Count < 9)
        {
            return null;
        }

        if (values[5] == 0 || values[6] == 0 || values[7] == 0 || values[8] == 0)
        {
            return 3;
        }

        return values[4] == 0 || values[0] == 0 || values[1] == 0 || values[2] == 0 || values[3] == 0
            ? 2
            : 1;
    }

    private static bool TryGetFinite(float? value, out float finiteValue)
    {
        finiteValue = value.GetValueOrDefault();
        return value.HasValue && float.IsFinite(finiteValue);
    }

    private static bool TryGetInverseAverage(IReadOnlyList<float> values, int count, out float result)
    {
        result = 0f;
        if (values.Count < count)
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            if (!float.IsFinite(values[index]))
            {
                return false;
            }

            result += 1f - values[index];
        }

        result /= count;
        return true;
    }

    private static bool TryGetInverseMinimum(IReadOnlyList<float> values, IReadOnlyList<int> indices, out float result)
    {
        result = 0f;
        if (indices.Count == 0)
        {
            return false;
        }

        var hasValue = false;
        foreach (var index in indices)
        {
            if (index >= values.Count || !float.IsFinite(values[index]))
            {
                return false;
            }

            var inverse = 1f - values[index];
            if (!hasValue || inverse < result)
            {
                result = inverse;
                hasValue = true;
            }
        }

        return hasValue;
    }

    private static bool TryGetMinimum(IReadOnlyList<float> values, IReadOnlyList<int> indices, out float result)
    {
        result = 0f;
        if (indices.Count == 0)
        {
            return false;
        }

        var hasValue = false;
        foreach (var index in indices)
        {
            if (index >= values.Count || !float.IsFinite(values[index]))
            {
                return false;
            }

            if (!hasValue || values[index] < result)
            {
                result = values[index];
                hasValue = true;
            }
        }

        return hasValue;
    }
}

internal sealed record RBRDamageTelemetryLogEntry(
    string Kind,
    IReadOnlyList<string> ChangedComponents,
    RBRDamageTelemetrySnapshot Snapshot);

internal sealed class RBRDamageTelemetryProbe
{
    private RBRDamageTelemetryDiagnostics _diagnostics = CreateInitialDiagnostics();
    private RBRDamageTelemetrySnapshot? _previousSnapshot;

    public void SetWaitingForRace()
    {
        if (_diagnostics.Status == "WaitingForRace")
        {
            return;
        }

        _diagnostics = _diagnostics with
        {
            Status = "WaitingForRace",
            LastError = string.Empty,
            LastChangedComponents = Array.Empty<string>()
        };
        _previousSnapshot = null;
    }

    public void SetStopped()
    {
        if (_diagnostics.Status != "Stopped")
        {
            _diagnostics = _diagnostics with
            {
                Status = "Stopped",
                LastError = string.Empty,
                LastChangedComponents = Array.Empty<string>()
            };
        }

        _previousSnapshot = null;
    }

    public bool ReportUnavailable(string error)
    {
        var isNewError = _diagnostics.Status != "Unavailable" ||
                         !string.Equals(_diagnostics.LastError, error, StringComparison.Ordinal);
        _diagnostics = _diagnostics with
        {
            Status = "Unavailable",
            LastError = error,
            LastChangedComponents = Array.Empty<string>(),
            LatestSnapshot = null
        };
        _previousSnapshot = null;
        return isNewError;
    }

    public RBRDamageTelemetryLogEntry? Record(RBRDamageTelemetrySnapshot snapshot)
    {
        var changes = RBRDamageTelemetryChangeDetector.GetChanges(_previousSnapshot, snapshot);
        var isBaseline = _previousSnapshot == null ||
                         changes.Contains("physicsPointer", StringComparer.Ordinal) ||
                         changes.Contains("damagePointer", StringComparer.Ordinal);
        var now = snapshot.CapturedAtUtc;
        var lastChanges = changes.Count > 0 ? changes : _diagnostics.LastChangedComponents;
        var lastChangeUtc = changes.Count > 0 ? now : _diagnostics.LastChangeUtc;

        _diagnostics = new RBRDamageTelemetryDiagnostics(
            "Active",
            string.Empty,
            _diagnostics.SnapshotCount + 1,
            now,
            lastChangeUtc,
            lastChanges,
            snapshot);
        _previousSnapshot = snapshot;

        if (!isBaseline && changes.Count == 0)
        {
            return null;
        }

        return new RBRDamageTelemetryLogEntry(
            isBaseline ? "baseline" : "change",
            changes,
            snapshot);
    }

    public RBRDamageTelemetryDiagnostics CaptureDiagnostics()
    {
        return _diagnostics;
    }

    private static RBRDamageTelemetryDiagnostics CreateInitialDiagnostics()
    {
        return new RBRDamageTelemetryDiagnostics(
            "NotStarted",
            string.Empty,
            0,
            null,
            null,
            Array.Empty<string>(),
            null);
    }
}

internal static class RBRDamageTelemetryChangeDetector
{
    public static IReadOnlyList<string> GetChanges(
        RBRDamageTelemetrySnapshot? previous,
        RBRDamageTelemetrySnapshot current)
    {
        if (previous == null)
        {
            return new[] { "baseline" };
        }

        var changes = new List<string>();
        if (previous.PhysicsAddress != current.PhysicsAddress)
        {
            changes.Add("physicsPointer");
        }
        if (previous.DamageAddress != current.DamageAddress)
        {
            changes.Add("damagePointer");
        }

        var previousParts = previous.Parts.ToDictionary(part => part.Name, StringComparer.Ordinal);
        foreach (var part in current.Parts)
        {
            if (!previousParts.TryGetValue(part.Name, out var previousPart) || HasSemanticChange(previousPart, part))
            {
                changes.Add($"part:{part.Name}");
            }
        }

        var previousGroups = previous.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal);
        foreach (var group in current.Groups)
        {
            if (!previousGroups.TryGetValue(group.Name, out var previousGroup) || HasSemanticChange(previousGroup, group))
            {
                changes.Add($"group:{group.Name}");
            }
        }

        return changes;
    }

    private static bool HasSemanticChange(RBRDamagePartTelemetry previous, RBRDamagePartTelemetry current)
    {
        return previous.IsReadable != current.IsReadable || previous.HudLevel != current.HudLevel;
    }

    private static bool HasSemanticChange(RBRDamageGroupTelemetry previous, RBRDamageGroupTelemetry current)
    {
        return previous.IsComplete != current.IsComplete ||
               previous.HudLevel != current.HudLevel ||
               !previous.AffectedParts.SequenceEqual(current.AffectedParts, StringComparer.Ordinal);
    }
}

internal static class RBRDamageTelemetryFormatter
{
    public static string Address(int address)
    {
        return $"0x{unchecked((uint)address):X8}";
    }
}
