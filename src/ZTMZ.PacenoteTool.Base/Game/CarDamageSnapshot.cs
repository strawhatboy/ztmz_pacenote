using System;
using System.Collections.Generic;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>
/// A game-independent damage data contract. It preserves source-specific raw
/// values while exposing a compact, stable projection for dashboards.
/// </summary>
public sealed record CarDamageSnapshot(
    string Source,
    DateTimeOffset CapturedAtUtc,
    CarDamageDashboardState Dashboard,
    IReadOnlyList<CarDamageGroupSnapshot> Groups,
    IReadOnlyList<CarDamageComponentSnapshot> Components,
    IReadOnlyList<CarDamageRawComponentSnapshot> RawComponents)
{
    public static CarDamageSnapshot Create(
        string source,
        DateTimeOffset capturedAtUtc,
        IEnumerable<CarDamageGroupSnapshot> groups,
        IEnumerable<CarDamageComponentSnapshot> components,
        IEnumerable<CarDamageRawComponentSnapshot> rawComponents)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(rawComponents);

        var normalizedComponents = components
            .GroupBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .Select(MergeComponents)
            .OrderBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedGroups = groups
            .GroupBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .Select(MergeGroups)
            .OrderBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedRawComponents = rawComponents
            .OrderBy(component => component.SourceGroup, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CarDamageSnapshot(
            source,
            capturedAtUtc,
            CreateDashboardState(normalizedGroups, normalizedComponents),
            normalizedGroups,
            normalizedComponents,
            normalizedRawComponents);
    }

    public static CarDamageSnapshot Unavailable(string source = "")
    {
        return new CarDamageSnapshot(
            source,
            default,
            CarDamageDashboardState.Unavailable,
            Array.Empty<CarDamageGroupSnapshot>(),
            Array.Empty<CarDamageComponentSnapshot>(),
            Array.Empty<CarDamageRawComponentSnapshot>());
    }

    private static CarDamageComponentSnapshot MergeComponents(
        IGrouping<string, CarDamageComponentSnapshot> components)
    {
        var first = components.First();
        return first with
        {
            IsAvailable = components.Any(component => component.IsAvailable),
            IsActive = components.Any(component => component.IsAvailable && component.IsActive),
            Severity = components.Where(c => c.IsAvailable).Select(c => c.Severity).DefaultIfEmpty(0).Max(),
            Magnitude = components.Where(c => c.IsAvailable).Select(c => c.Magnitude).DefaultIfEmpty(0).Max()
        };
    }

    private static CarDamageGroupSnapshot MergeGroups(
        IGrouping<string, CarDamageGroupSnapshot> groups)
    {
        var first = groups.First();
        return first with
        {
            IsAvailable = groups.Any(group => group.IsAvailable),
            IsComplete = groups.All(group => group.IsComplete),
            Severity = groups.Where(g => g.IsAvailable).Select(g => g.Severity).DefaultIfEmpty(0).Max(),
            AffectedComponentIds = groups
                .SelectMany(group => group.AffectedComponentIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(componentId => componentId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static CarDamageDashboardState CreateDashboardState(
        IReadOnlyList<CarDamageGroupSnapshot> groups,
        IReadOnlyList<CarDamageComponentSnapshot> components)
    {
        var groupsById = groups.ToDictionary(group => group.Id, StringComparer.OrdinalIgnoreCase);
        var componentsById = components.ToDictionary(component => component.Id, StringComparer.OrdinalIgnoreCase);

        var isAvailable = groups.Any(group => group.IsAvailable)
            || components.Any(component => component.IsAvailable);
        if (!isAvailable)
        {
            return CarDamageDashboardState.Unavailable;
        }

        return new CarDamageDashboardState
        {
            IsAvailable = true,
            EngineAvailable = components.Any(c => c.IsAvailable && c.Kind == "engine") || groups.Any(g => g.IsAvailable && g.Id == "engine"),
            RadiatorAvailable = components.Any(c => c.IsAvailable && c.Kind == "radiator") || groups.Any(g => g.IsAvailable && g.Id == "radiator"),
            DifferentialAvailable = components.Any(c => c.IsAvailable && c.Kind == "differential") || groups.Any(g => g.IsAvailable && g.Id == "differential"),
            FrontBumperAvailable = components.Any(c => c.IsAvailable && c.Kind == "front-bumper"),
            RearBumperAvailable = components.Any(c => c.IsAvailable && c.Kind == "rear-bumper"),
            LeftDoorAvailable = components.Any(c => c.IsAvailable && c.Kind == "left-door"),
            RightDoorAvailable = components.Any(c => c.IsAvailable && c.Kind == "right-door"),
            FrontLeftWheelAvailable = components.Any(c => c.IsAvailable && c.Kind == "wheel" && c.Location == "front-left"),
            FrontLeftBrakeAvailable = components.Any(c => c.IsAvailable && c.Kind == "brake" && c.Location == "front-left"),
            FrontLeftSuspensionAvailable = components.Any(c => c.IsAvailable && c.Kind == "suspension" && c.Location == "front-left"),
            FrontLeftTyreAvailable = components.Any(c => c.IsAvailable && c.Kind == "tyre" && c.Location == "front-left"),
            FrontRightWheelAvailable = components.Any(c => c.IsAvailable && c.Kind == "wheel" && c.Location == "front-right"),
            FrontRightBrakeAvailable = components.Any(c => c.IsAvailable && c.Kind == "brake" && c.Location == "front-right"),
            FrontRightSuspensionAvailable = components.Any(c => c.IsAvailable && c.Kind == "suspension" && c.Location == "front-right"),
            FrontRightTyreAvailable = components.Any(c => c.IsAvailable && c.Kind == "tyre" && c.Location == "front-right"),
            RearLeftWheelAvailable = components.Any(c => c.IsAvailable && c.Kind == "wheel" && c.Location == "rear-left"),
            RearLeftBrakeAvailable = components.Any(c => c.IsAvailable && c.Kind == "brake" && c.Location == "rear-left"),
            RearLeftSuspensionAvailable = components.Any(c => c.IsAvailable && c.Kind == "suspension" && c.Location == "rear-left"),
            RearLeftTyreAvailable = components.Any(c => c.IsAvailable && c.Kind == "tyre" && c.Location == "rear-left"),
            RearRightWheelAvailable = components.Any(c => c.IsAvailable && c.Kind == "wheel" && c.Location == "rear-right"),
            RearRightBrakeAvailable = components.Any(c => c.IsAvailable && c.Kind == "brake" && c.Location == "rear-right"),
            RearRightSuspensionAvailable = components.Any(c => c.IsAvailable && c.Kind == "suspension" && c.Location == "rear-right"),
            RearRightTyreAvailable = components.Any(c => c.IsAvailable && c.Kind == "tyre" && c.Location == "rear-right"),
            GearboxSeverity = GetKindSeverity(components, CarDamageIds.Gearbox),
            BodyworkSeverity = GetKindSeverity(components, CarDamageIds.Bodywork),
            GearboxAvailable = components.Any(c => c.IsAvailable && c.Kind == CarDamageIds.Gearbox),
            BodyworkAvailable = components.Any(c => c.IsAvailable && c.Kind == CarDamageIds.Bodywork),
            EngineSeverity = GetGroupOrComponentSeverity(
                groupsById,
                componentsById,
                CarDamageIds.Engine,
                CarDamageIds.Engine),
            RadiatorSeverity = GetGroupOrComponentSeverity(
                groupsById,
                componentsById,
                CarDamageIds.Radiator,
                CarDamageIds.Radiator),
            DifferentialSeverity = GetGroupOrComponentSeverity(
                groupsById,
                componentsById,
                CarDamageIds.Differential,
                CarDamageIds.Differential),
            FrontRightSeverity = GetGroupSeverity(groupsById, CarDamageIds.FrontRight),
            RearRightSeverity = GetGroupSeverity(groupsById, CarDamageIds.RearRight),
            RearLeftSeverity = GetGroupSeverity(groupsById, CarDamageIds.RearLeft),
            FrontLeftSeverity = GetGroupSeverity(groupsById, CarDamageIds.FrontLeft),
            FrontRightWheelSeverity = GetComponentSeverity(componentsById, CarDamageIds.Wheel, CarDamageIds.FrontRight),
            RearRightWheelSeverity = GetComponentSeverity(componentsById, CarDamageIds.Wheel, CarDamageIds.RearRight),
            RearLeftWheelSeverity = GetComponentSeverity(componentsById, CarDamageIds.Wheel, CarDamageIds.RearLeft),
            FrontLeftWheelSeverity = GetComponentSeverity(componentsById, CarDamageIds.Wheel, CarDamageIds.FrontLeft),
            FrontRightBrakeSeverity = GetComponentSeverity(componentsById, CarDamageIds.Brake, CarDamageIds.FrontRight),
            RearRightBrakeSeverity = GetComponentSeverity(componentsById, CarDamageIds.Brake, CarDamageIds.RearRight),
            RearLeftBrakeSeverity = GetComponentSeverity(componentsById, CarDamageIds.Brake, CarDamageIds.RearLeft),
            FrontLeftBrakeSeverity = GetComponentSeverity(componentsById, CarDamageIds.Brake, CarDamageIds.FrontLeft),
            FrontRightSuspensionSeverity = GetComponentSeverity(componentsById, CarDamageIds.Suspension, CarDamageIds.FrontRight),
            RearRightSuspensionSeverity = GetComponentSeverity(componentsById, CarDamageIds.Suspension, CarDamageIds.RearRight),
            RearLeftSuspensionSeverity = GetComponentSeverity(componentsById, CarDamageIds.Suspension, CarDamageIds.RearLeft),
            FrontLeftSuspensionSeverity = GetComponentSeverity(componentsById, CarDamageIds.Suspension, CarDamageIds.FrontLeft),
            FrontRightTyreSeverity = GetComponentSeverity(componentsById, CarDamageIds.Tyre, CarDamageIds.FrontRight),
            RearRightTyreSeverity = GetComponentSeverity(componentsById, CarDamageIds.Tyre, CarDamageIds.RearRight),
            RearLeftTyreSeverity = GetComponentSeverity(componentsById, CarDamageIds.Tyre, CarDamageIds.RearLeft),
            FrontLeftTyreSeverity = GetComponentSeverity(componentsById, CarDamageIds.Tyre, CarDamageIds.FrontLeft),
            FrontBumperSeverity = GetComponentSeverity(componentsById, CarDamageIds.FrontBumper),
            RearBumperSeverity = GetComponentSeverity(componentsById, CarDamageIds.RearBumper),
            LeftDoorSeverity = GetComponentSeverity(componentsById, CarDamageIds.LeftDoor),
            RightDoorSeverity = GetComponentSeverity(componentsById, CarDamageIds.RightDoor),
            GeneralSeverity = Math.Max(
                groups.Where(group => group.IsAvailable).DefaultIfEmpty().Max(group => group?.Severity ?? 0),
                components.Where(component => component.IsAvailable).DefaultIfEmpty().Max(component => component?.Severity ?? 0))
        };
    }

    private static int GetKindSeverity(IEnumerable<CarDamageComponentSnapshot> components, string kind)
        => components.Where(c => c.IsAvailable && string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Severity).DefaultIfEmpty(0).Max();

    private static int GetGroupOrComponentSeverity(
        IReadOnlyDictionary<string, CarDamageGroupSnapshot> groups,
        IReadOnlyDictionary<string, CarDamageComponentSnapshot> components,
        string groupId,
        string componentKind)
    {
        return groups.TryGetValue(groupId, out var group) && group.IsAvailable
            ? group.Severity
            : GetComponentSeverity(components, componentKind);
    }

    private static int GetGroupSeverity(
        IReadOnlyDictionary<string, CarDamageGroupSnapshot> groups,
        string id)
    {
        return groups.TryGetValue(id, out var group) && group.IsAvailable ? group.Severity : 0;
    }

    private static int GetComponentSeverity(
        IReadOnlyDictionary<string, CarDamageComponentSnapshot> components,
        string kind,
        string location = "")
    {
        return components.TryGetValue(CarDamageIds.ComponentId(kind, location), out var component)
            && component.IsAvailable
            ? component.Severity
            : 0;
    }
}

/// <summary>
/// The compact field-based projection consumed by Lua dashboards. Severity uses
/// a 0-4 range where zero is healthy and four is critical.
/// </summary>
public sealed record CarDamageDashboardState
{
    public static CarDamageDashboardState Unavailable { get; } = new();

    public bool IsAvailable { get; init; }
    public bool EngineAvailable { get; init; }
    public bool RadiatorAvailable { get; init; }
    public bool DifferentialAvailable { get; init; }
    public bool FrontBumperAvailable { get; init; }
    public bool RearBumperAvailable { get; init; }
    public bool LeftDoorAvailable { get; init; }
    public bool RightDoorAvailable { get; init; }
    public bool FrontLeftWheelAvailable { get; init; }
    public bool FrontLeftBrakeAvailable { get; init; }
    public bool FrontLeftSuspensionAvailable { get; init; }
    public bool FrontLeftTyreAvailable { get; init; }
    public bool FrontRightWheelAvailable { get; init; }
    public bool FrontRightBrakeAvailable { get; init; }
    public bool FrontRightSuspensionAvailable { get; init; }
    public bool FrontRightTyreAvailable { get; init; }
    public bool RearLeftWheelAvailable { get; init; }
    public bool RearLeftBrakeAvailable { get; init; }
    public bool RearLeftSuspensionAvailable { get; init; }
    public bool RearLeftTyreAvailable { get; init; }
    public bool RearRightWheelAvailable { get; init; }
    public bool RearRightBrakeAvailable { get; init; }
    public bool RearRightSuspensionAvailable { get; init; }
    public bool RearRightTyreAvailable { get; init; }
    public int GeneralSeverity { get; init; }
    public int GearboxSeverity { get; init; }
    public int BodyworkSeverity { get; init; }
    public bool GearboxAvailable { get; init; }
    public bool BodyworkAvailable { get; init; }
    public int EngineSeverity { get; init; }
    public int RadiatorSeverity { get; init; }
    public int DifferentialSeverity { get; init; }
    public int FrontRightSeverity { get; init; }
    public int RearRightSeverity { get; init; }
    public int RearLeftSeverity { get; init; }
    public int FrontLeftSeverity { get; init; }
    public int FrontRightWheelSeverity { get; init; }
    public int RearRightWheelSeverity { get; init; }
    public int RearLeftWheelSeverity { get; init; }
    public int FrontLeftWheelSeverity { get; init; }
    public int FrontRightBrakeSeverity { get; init; }
    public int RearRightBrakeSeverity { get; init; }
    public int RearLeftBrakeSeverity { get; init; }
    public int FrontLeftBrakeSeverity { get; init; }
    public int FrontRightSuspensionSeverity { get; init; }
    public int RearRightSuspensionSeverity { get; init; }
    public int RearLeftSuspensionSeverity { get; init; }
    public int FrontLeftSuspensionSeverity { get; init; }
    public int FrontRightTyreSeverity { get; init; }
    public int RearRightTyreSeverity { get; init; }
    public int RearLeftTyreSeverity { get; init; }
    public int FrontLeftTyreSeverity { get; init; }
    public int FrontBumperSeverity { get; init; }
    public int RearBumperSeverity { get; init; }
    public int LeftDoorSeverity { get; init; }
    public int RightDoorSeverity { get; init; }
}

/// <summary>
/// One normalized aggregate group. RBR exposes seven groups; other games can
/// populate only the groups their telemetry supports.
/// </summary>
public sealed record CarDamageGroupSnapshot(
    string Id,
    bool IsAvailable,
    bool IsComplete,
    int Severity,
    IReadOnlyList<string> AffectedComponentIds);

/// <summary>
/// One normalized physical component used by dashboard artwork.
/// </summary>
public sealed record CarDamageComponentSnapshot(
    string Id,
    string Kind,
    string Location,
    bool IsAvailable,
    bool IsActive,
    int Severity,
    float Magnitude);

/// <summary>
/// Loss-minimized source data retained for diagnostics and future dashboards.
/// </summary>
public sealed record CarDamageRawComponentSnapshot(
    string Id,
    string SourceGroup,
    string SourceName,
    bool IsReadable,
    bool IsActive,
    int Severity,
    float? Magnitude,
    float? FloatValue,
    int? IntegerValue,
    IReadOnlyList<float> FloatValues,
    IReadOnlyList<int> IntegerValues,
    string RawValue);

/// <summary>
/// Stable group, component, and location identifiers shared by game adapters.
/// </summary>
public static class CarDamageIds
{
    public const string Engine = "engine";
    public const string Gearbox = "gearbox";
    public const string Radiator = "radiator";
    public const string Differential = "differential";
    public const string Wheel = "wheel";
    public const string Brake = "brake";
    public const string Suspension = "suspension";
    public const string Tyre = "tyre";
    public const string FrontBumper = "front-bumper";
    public const string RearBumper = "rear-bumper";
    public const string LeftDoor = "left-door";
    public const string RightDoor = "right-door";
    public const string Bodywork = "bodywork";
    public const string FrontRight = "front-right";
    public const string RearRight = "rear-right";
    public const string RearLeft = "rear-left";
    public const string FrontLeft = "front-left";

    public static string ComponentId(string kind, string location = "")
    {
        return string.IsNullOrWhiteSpace(location) ? kind : $"{kind}:{location}";
    }
}
