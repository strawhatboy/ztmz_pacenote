using System;
using System.Collections.Generic;
using System.Linq;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

/// <summary>Verified native component damage, not collision-derived estimates.</summary>
public static class Dr2CarDamage
{
    public static CarDamageSnapshot? Read(Func<ulong, int, byte[]> read, ulong physics)
    {
        ulong Pointer(ulong address) => BitConverter.ToUInt64(read(address, 8));
        // 0x7315c0 -> 0x7323f0 -> 0x751260. The embedded interface points
        // back to its owning damage state, whose first eight bytes are not floats.
        var owner = physics + 0x2bd0;
        if (Pointer(owner) != owner) return null;
        var parameters = Pointer(owner + 0x440);
        if (parameters < 0x10000) return null;
        var data = read(owner + 8, 0xb0);
        var config = read(parameters, 0x33c);
        float Value(byte[] bytes, int offset) => BitConverter.ToSingle(bytes, offset);
        var items = new[]
        {
            (CarDamageIds.Engine, Value(data, 0), Math.Min(Value(config, 0x1f4),
                Math.Min(Value(config, 0x1fc), Value(config, 0x204)))),
            (CarDamageIds.Radiator, Value(data, 0x80), Value(config, 0x1ec)),
            (CarDamageIds.Gearbox, Math.Max(Value(data, 0x0c), Value(data, 0x84)),
                Math.Min(Value(config, 0x2a0), Value(config, 0x334)))
        };
        var components = new List<CarDamageComponentSnapshot>();
        var raw = new List<CarDamageRawComponentSnapshot>();
        bool Valid(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
        bool Add(string kind, string location, string sourceName, float damage, float onset, int minimumSeverity = 0)
        {
            if (!Valid(damage) || !Valid(onset)) return false;
            var effect = onset < 1 ? Math.Clamp((damage - onset) / (1 - onset), 0, 1)
                : damage >= onset ? 1f : 0f;
            var severity = Math.Max(minimumSeverity, effect <= 0 ? 0 : Math.Min(4, (int)Math.Ceiling(effect * 4)));
            var id = CarDamageIds.ComponentId(kind, location);
            components.Add(new(id, kind, location, true, severity > 0, severity, damage));
            raw.Add(new(CarDamageIds.ComponentId(sourceName, location), "DR2", sourceName,
                true, severity > 0, severity, damage, damage, null, [damage, onset, effect], [],
                FormattableString.Invariant($"damage={damage}; effectOnset={onset}; effect={effect}")));
            return true;
        }
        foreach (var (kind, damage, onset) in items)
        {
            // Native 0x743570: effects start at the vehicle-specific threshold.
            // The 0-4 severity buckets are ZTMZ's projection, not a native enum.
            if (!Add(kind, "", kind, damage, onset)) return null;
        }

        // Component 0 (Bodywork): native 0x751288, no performance-onset threshold.
        Add(CarDamageIds.Bodywork, "", "bodywork", Math.Max(Value(data, 0xa4), Value(data, 0xa8)), 0);
        // Native wheel indices also used by puncture voice routing (0xb2108e).
        string[] locations = [CarDamageIds.RearLeft, CarDamageIds.RearRight,
            CarDamageIds.FrontLeft, CarDamageIds.FrontRight];
        // 0x732190 -> 0x57d550 returns the discrete tyre state, not tyre wear.
        var tyreStates = read(owner + 0x120, 4);
        for (var wheel = 0; wheel < locations.Length; wheel++)
        {
            var location = locations[wheel];
            var offset = wheel * 4;
            // 0x751a60 explicitly combines these two sources as suspension.
            Add(CarDamageIds.Suspension, location, "damper", Value(data, 0x10 + offset), Value(config, 0x84));
            Add(CarDamageIds.Suspension, location, "spring", Value(data, 0x20 + offset), Value(config, 0xb8));
            // 0xb18375 requests effect 6 for damage_brake.
            Add(CarDamageIds.Brake, location, "brake", Value(data, 0x30 + offset), Value(config, 0x18c));
            // Effect types 2–5 use the same wheel-impact value, with different
            // geometry effects. Type 8 tests the wheel failure threshold.
            var wheelDamage = Value(data, 0x8c + offset);
            var wheelOnset = Math.Min(Math.Min(Value(config, 0xc0), Value(config, 0xec)),
                Math.Min(Value(config, 0xf4), Value(config, 0xfc)));
            var wheelFailure = Value(config, 0x104);
            Add(CarDamageIds.Wheel, location, "wheel-impact", wheelDamage, wheelOnset,
                Valid(wheelFailure) && wheelDamage >= wheelFailure ? 4 : 0);
            var tyreState = tyreStates[wheel];
            if (tyreState > 2) continue;
            var tyreSeverity = tyreState == 0 ? 0 : tyreState == 1 ? 3 : 4;
            var tyreId = CarDamageIds.ComponentId(CarDamageIds.Tyre, location);
            components.Add(new(tyreId, CarDamageIds.Tyre, location, true, tyreState != 0,
                tyreSeverity, tyreState == 0 ? 0 : 1));
            raw.Add(new(tyreId, "DR2", "tyre-state", true, tyreState != 0, tyreSeverity,
                null, null, tyreState, [], [tyreState], $"state={tyreState}"));
        }
        if (Pointer(owner) != owner || Pointer(owner + 0x440) != parameters) return null;
        var groups = components.Where(c => c.Location.Length != 0).GroupBy(c => c.Location)
            .Select(g => new CarDamageGroupSnapshot(g.Key, true, false, g.Max(c => c.Severity),
                g.Where(c => c.IsActive).Select(c => c.Id).Distinct().ToArray()));
        return CarDamageSnapshot.Create("DR2", DateTimeOffset.UtcNow, groups, components, raw);
    }
}
