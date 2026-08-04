using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRDamageTelemetryProbeTest
{
    [Fact]
    public void FirstSnapshotCreatesBaselineDiagnostics()
    {
        var probe = new RBRDamageTelemetryProbe();

        var entry = probe.Record(CreateSnapshot());

        Assert.NotNull(entry);
        Assert.Equal("baseline", entry!.Kind);
        Assert.Equal(new[] { "baseline" }, entry.ChangedComponents);
        Assert.Equal("Active", probe.CaptureDiagnostics().Status);
        Assert.Equal(1, probe.CaptureDiagnostics().SnapshotCount);
    }

    [Fact]
    public void RawPartValuesAndRbrHudGroupsAreBothRetained()
    {
        var parts = new[]
        {
            CreateFloatPart("Radiator", 0.6f),
            CreateIntegerPart("WaterPump", 0),
            CreateFloatPart("Intercooler", 0f),
            CreateFloatArrayPart("TyreLF", Enumerable.Repeat(0.75f, 32).ToArray())
        };
        var snapshot = CreateSnapshot(parts);
        var radiator = snapshot.Parts.Single(part => part.Name == "Radiator");
        var tyre = snapshot.Parts.Single(part => part.Name == "TyreLF");
        var radiatorGroup = snapshot.Groups.Single(group => group.Name == "Radiator");

        Assert.Equal(0.6f, radiator.FloatValue);
        Assert.Equal(5, radiator.HudLevel);
        Assert.Equal(32, tyre.FloatValues.Count);
        Assert.Equal(0.75f, tyre.FloatValues[0]);
        Assert.True(radiatorGroup.IsComplete);
        Assert.Equal(5, radiatorGroup.HudLevel);
        Assert.Equal(4, radiatorGroup.Severity);
        Assert.Equal(new[] { "Radiator", "WaterPump" }, radiatorGroup.AffectedParts);
        Assert.Equal(
            new[] { "Radiator", "Engine", "Diff", "FrontRight", "RearRight", "RearLeft", "FrontLeft" },
            snapshot.Groups.Select(group => group.Name));
    }

    [Fact]
    public void EngineGroupUsesItsWorstPart()
    {
        var parts = new[]
        {
            CreatePartWithHudLevel("Battery", 1),
            CreatePartWithHudLevel("Clutch", 2),
            CreatePartWithHudLevel("GearActuator", 1),
            CreatePartWithHudLevel("Gearbox", 3),
            CreatePartWithHudLevel("HeadGasket", 1),
            CreatePartWithHudLevel("OilPump", 5),
            CreatePartWithHudLevel("OilCooler", 1),
            CreatePartWithHudLevel("Starter", 1)
        };

        var engine = RBRDamageTelemetryCatalog.CreateGroups(parts)
            .Single(group => group.Name == "Engine");

        Assert.True(engine.IsComplete);
        Assert.Equal(5, engine.HudLevel);
        Assert.Equal(4, engine.Severity);
        Assert.Equal(new[] { "Clutch", "Gearbox", "OilPump" }, engine.AffectedParts);
    }

    [Fact]
    public void CatalogProvidesEveryPartRequiredByTheSevenGroups()
    {
        var parts = RBRDamageTelemetryCatalog.PartDefinitions
            .Select(definition => CreatePartWithHudLevel(definition.Name, 1))
            .ToArray();

        var groups = RBRDamageTelemetryCatalog.CreateGroups(parts);

        Assert.Equal(7, groups.Count);
        Assert.All(groups, group => Assert.True(group.IsComplete, group.Name));
    }

    [Fact]
    public void RawChangesWithinTheSameLevelDoNotCreateLogNoise()
    {
        var probe = new RBRDamageTelemetryProbe();
        var baseline = CreateSnapshot(new[] { CreateFloatPart("Radiator", 0.1f) });
        probe.Record(baseline);

        var entry = probe.Record(CreateSnapshot(
            new[] { CreateFloatPart("Radiator", 0.15f) },
            baseline.CapturedAtUtc.AddMilliseconds(250)));

        Assert.Null(entry);
        var latestPart = probe.CaptureDiagnostics().LatestSnapshot!.Parts.Single(part => part.Name == "Radiator");
        Assert.Equal(0.15f, latestPart.FloatValue);
        Assert.Equal(3, latestPart.HudLevel);
    }

    [Fact]
    public void DamagePointerChangeCreatesNewBaseline()
    {
        var probe = new RBRDamageTelemetryProbe();
        var baseline = CreateSnapshot();
        probe.Record(baseline);

        var entry = probe.Record(baseline with
        {
            CapturedAtUtc = baseline.CapturedAtUtc.AddMilliseconds(250),
            DamageAddress = baseline.DamageAddress + 0x100
        });

        Assert.NotNull(entry);
        Assert.Equal("baseline", entry!.Kind);
        Assert.Contains("damagePointer", entry.ChangedComponents);
    }

    [Fact]
    public void TelemetrySnapshotCanBeSerializedForTheSupportBundle()
    {
        var probe = new RBRDamageTelemetryProbe();
        probe.Record(CreateSnapshot(new[]
        {
            CreateFloatPart("Radiator", 0.6f),
            CreateIntegerPart("WaterPump", 0),
            CreateFloatPart("Intercooler", 0f)
        }));

        var json = JsonSerializer.Serialize(probe.CaptureDiagnostics());

        Assert.Contains("LatestSnapshot", json);
        Assert.Contains("Parts", json);
        Assert.Contains("Groups", json);
        Assert.Contains("Radiator", json);
    }

    [Fact]
    public void LegacyDiagnosticSettingIsRemovedWithItsAlignedMetadata()
    {
        var legacy = new CommonGameConfigs
        {
            PropertyName = new Dictionary<string, string>
            {
                ["play"] = "play",
                ["definition"] = "definition",
                ["auto"] = "auto",
                [RBRGameConfigurationMigration.DamageTelemetryDiagnosticsConfigKey] = "diagnostics"
            },
            PropertyValue = new List<object> { true, "jannemod_v3.zdb", true, false },
            PropertyType = new List<string> { "bool", "file:zdb", "bool", "bool" },
            ValueRange = new List<List<double>>
            {
                new(), new(), new(), new()
            }
        };
        var current = new CommonGameConfigs
        {
            PropertyName = new Dictionary<string, string>
            {
                ["play"] = "play",
                ["definition"] = "definition",
                ["auto"] = "auto"
            },
            PropertyType = new List<string> { "bool", "file:zdb", "bool" }
        };

        var changed = RBRGameConfigurationMigration.Migrate(legacy, current);

        Assert.True(changed);
        Assert.DoesNotContain(RBRGameConfigurationMigration.DamageTelemetryDiagnosticsConfigKey, legacy.PropertyName.Keys);
        Assert.Equal(3, legacy.PropertyName.Count);
        Assert.Equal(3, legacy.PropertyValue.Count);
        Assert.Equal(3, legacy.PropertyType.Count);
        Assert.Equal(3, legacy.ValueRange.Count);
        Assert.Equal(new[] { "bool", "file:zdb", "bool" }, legacy.PropertyType);
    }

    private static RBRDamageTelemetrySnapshot CreateSnapshot(
        IReadOnlyList<RBRDamagePartTelemetry>? parts = null,
        DateTimeOffset? capturedAtUtc = null)
    {
        var actualParts = parts ?? new[]
        {
            CreateFloatPart("Radiator", 0f),
            CreateIntegerPart("WaterPump", 1),
            CreateFloatPart("Intercooler", 0f)
        };

        return new RBRDamageTelemetrySnapshot(
            capturedAtUtc ?? new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            0x008EF660,
            0x01000000,
            actualParts,
            RBRDamageTelemetryCatalog.CreateGroups(actualParts));
    }

    private static RBRDamagePartTelemetry CreateFloatPart(string name, float value)
    {
        return RBRDamageTelemetryCatalog.CreatePart(
            GetDefinition(name),
            0x01000000,
            true,
            floatValue: value);
    }

    private static RBRDamagePartTelemetry CreateIntegerPart(string name, int value)
    {
        return RBRDamageTelemetryCatalog.CreatePart(
            GetDefinition(name),
            0x01000000,
            true,
            integerValue: value);
    }

    private static RBRDamagePartTelemetry CreateFloatArrayPart(string name, IReadOnlyList<float> values)
    {
        return RBRDamageTelemetryCatalog.CreatePart(
            GetDefinition(name),
            0x01000000,
            true,
            floatValues: values);
    }

    private static RBRDamagePartTelemetry CreatePartWithHudLevel(string name, int hudLevel)
    {
        return new RBRDamagePartTelemetry(
            name,
            0x01000000,
            true,
            null,
            null,
            Array.Empty<float>(),
            Array.Empty<int>(),
            hudLevel);
    }

    private static RBRDamagePartDefinition GetDefinition(string name)
    {
        return RBRDamageTelemetryCatalog.PartDefinitions.Single(definition => definition.Name == name);
    }
}
