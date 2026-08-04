using System;
using System.Collections.Generic;
using System.Linq;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

/// <summary>
/// Projects RBR's recovered RBRHUD-compatible data into the shared dashboard
/// contract without discarding the source values retained by diagnostics.
/// </summary>
public static class RBRCarDamageSnapshotFactory
{
    public static CarDamageSnapshot? Create(RBRDamageTelemetrySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var rawByName = snapshot.Parts.ToDictionary(part => part.Name, StringComparer.OrdinalIgnoreCase);
        var components = snapshot.Parts
            .Select(CreateComponent)
            .Where(component => component is not null)
            .Cast<CarDamageComponentSnapshot>()
            .ToArray();
        var groups = snapshot.Groups
            .Select(group => CreateGroup(group, rawByName, components))
            .ToArray();
        var rawComponents = snapshot.Parts
            .Select(CreateRawComponent)
            .ToArray();

        return CarDamageSnapshot.Create(
            "rbr",
            snapshot.CapturedAtUtc,
            groups,
            components,
            rawComponents);
    }

    private static CarDamageGroupSnapshot CreateGroup(
        RBRDamageGroupTelemetry group,
        IReadOnlyDictionary<string, RBRDamagePartTelemetry> rawByName,
        IReadOnlyList<CarDamageComponentSnapshot> components)
    {
        var componentIds = group.PartNames
            .Select(TryGetComponentId)
            .Where(componentId => componentId is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var availableParts = group.PartNames
            .Where(rawByName.ContainsKey)
            .Select(partName => rawByName[partName])
            .Where(part => part.IsReadable)
            .ToArray();
        var fallbackSeverity = componentIds
            .Select(componentId => components.FirstOrDefault(component =>
                string.Equals(component.Id, componentId, StringComparison.OrdinalIgnoreCase)))
            .Where(component => component is not null)
            .Select(component => component!.Severity)
            .DefaultIfEmpty(0)
            .Max();

        return new CarDamageGroupSnapshot(
            GetGroupId(group.Name),
            availableParts.Length > 0,
            group.IsComplete,
            group.Severity ?? fallbackSeverity,
            componentIds);
    }

    private static CarDamageRawComponentSnapshot CreateRawComponent(RBRDamagePartTelemetry part)
    {
        var severity = part.Severity ?? 0;
        return new CarDamageRawComponentSnapshot(
            part.Name,
            "rbr",
            part.Name,
            part.IsReadable,
            severity > 0,
            severity,
            part.FloatValue ?? (part.Severity is { } level ? (float)level : null),
            part.FloatValue,
            part.IntegerValue,
            part.FloatValues.ToArray(),
            part.IntegerValues.ToArray(),
            string.Empty);
    }

    private static CarDamageComponentSnapshot? CreateComponent(RBRDamagePartTelemetry part)
    {
        if (!TryDescribeComponent(part.Name, out var kind, out var location))
        {
            return null;
        }

        var severity = part.Severity ?? 0;
        return new CarDamageComponentSnapshot(
            CarDamageIds.ComponentId(kind, location),
            kind,
            location,
            part.IsReadable,
            severity > 0,
            severity,
            part.FloatValue ?? severity);
    }

    private static string? TryGetComponentId(string partName)
    {
        return TryDescribeComponent(partName, out var kind, out var location)
            ? CarDamageIds.ComponentId(kind, location)
            : null;
    }

    private static bool TryDescribeComponent(string partName, out string kind, out string location)
    {
        location = GetLocation(partName);
        if (partName.StartsWith("Brake", StringComparison.OrdinalIgnoreCase))
        {
            kind = CarDamageIds.Brake;
            return true;
        }

        if (partName.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase))
        {
            kind = CarDamageIds.Wheel;
            return true;
        }

        if (partName.StartsWith("Tyre", StringComparison.OrdinalIgnoreCase))
        {
            kind = CarDamageIds.Tyre;
            return true;
        }

        if (partName.Contains("Damper", StringComparison.OrdinalIgnoreCase)
            || partName.Contains("Suspension", StringComparison.OrdinalIgnoreCase)
            || partName.Contains("ControlArm", StringComparison.OrdinalIgnoreCase)
            || partName.Contains("SteeringRod", StringComparison.OrdinalIgnoreCase)
            || partName.Contains("Hub", StringComparison.OrdinalIgnoreCase)
            || partName.Contains("DriveShaft", StringComparison.OrdinalIgnoreCase))
        {
            kind = CarDamageIds.Suspension;
            return true;
        }

        if (partName.Contains("Diff", StringComparison.OrdinalIgnoreCase))
        {
            kind = CarDamageIds.Differential;
            location = string.Empty;
            return true;
        }

        if (partName is "Radiator" or "WaterPump" or "Intercooler")
        {
            kind = CarDamageIds.Radiator;
            location = string.Empty;
            return true;
        }

        if (partName is "FrontBumper")
        {
            kind = CarDamageIds.FrontBumper;
            location = string.Empty;
            return true;
        }

        if (partName is "RearBumper")
        {
            kind = CarDamageIds.RearBumper;
            location = string.Empty;
            return true;
        }

        if (partName.StartsWith("LeftDoor", StringComparison.OrdinalIgnoreCase))
        {
            kind = CarDamageIds.LeftDoor;
            location = string.Empty;
            return true;
        }

        if (partName.StartsWith("RightDoor", StringComparison.OrdinalIgnoreCase))
        {
            kind = CarDamageIds.RightDoor;
            location = string.Empty;
            return true;
        }

        if (partName is "Bonnet" or "BonnetLock" or "Wing" or "Windshield" or "Bodywork")
        {
            kind = CarDamageIds.Bodywork;
            location = string.Empty;
            return true;
        }

        if (partName is "Gearbox" or "GearActuator" or "Starter" or "Clutch" or "Battery"
            or "HeadGasket" or "OilPump" or "OilCooler" or "Electrical" or "Hydraulics"
            or "Exhaust" or "ExhaustMount")
        {
            kind = CarDamageIds.Engine;
            location = string.Empty;
            return true;
        }

        kind = string.Empty;
        location = string.Empty;
        return false;
    }

    private static string GetGroupId(string name)
    {
        return name switch
        {
            "Radiator" => CarDamageIds.Radiator,
            "Engine" => CarDamageIds.Engine,
            "Diff" => CarDamageIds.Differential,
            "FrontRight" => CarDamageIds.FrontRight,
            "RearRight" => CarDamageIds.RearRight,
            "RearLeft" => CarDamageIds.RearLeft,
            "FrontLeft" => CarDamageIds.FrontLeft,
            _ => name
        };
    }

    private static string GetLocation(string partName)
    {
        return partName.EndsWith("LF", StringComparison.OrdinalIgnoreCase)
            ? CarDamageIds.FrontLeft
            : partName.EndsWith("RF", StringComparison.OrdinalIgnoreCase)
                ? CarDamageIds.FrontRight
                : partName.EndsWith("LR", StringComparison.OrdinalIgnoreCase)
                    ? CarDamageIds.RearLeft
                    : partName.EndsWith("RR", StringComparison.OrdinalIgnoreCase)
                        ? CarDamageIds.RearRight
                        : string.Empty;
    }
}
