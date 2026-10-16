using System.Buffers.Binary;
using System.Text;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

internal sealed record Wrc9DamagePart(string Name, float Maximum, float Lost, float WarningHealth, float CriticalHealth)
{
    internal float Magnitude => Lost / Maximum;
    // Native severity calculation at 0x7287fc compares remaining life with
    // per-part thresholds. These are not percentages chosen by ZTMZ.
    internal int Severity => 1 - Magnitude <= CriticalHealth ? 2 : 1 - Magnitude <= WarningHealth ? 1 : 0;
}

internal static class Wrc9Damage
{
    internal static CarDamageSnapshot? Read(Func<ulong, int, byte[]> read, ulong image, ulong participant, ulong vehicle)
    {
        try
        {
            ulong Pointer(ulong p) => BinaryPrimitives.ReadUInt64LittleEndian(read(p, 8));
            var owner = Pointer(participant + 0xc78);
            if (owner == 0 || Pointer(owner) != image + 0x1401488 || Pointer(owner + 0x40) != vehicle || Pointer(owner + 0xf8) != participant) return null;
            var head = Pointer(owner + 0xd0); var count = Pointer(owner + 0xd8);
            if (head == 0 || count is < 1 or > 64) return null;
            var seen = new HashSet<ulong>(); var names = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<ulong>(); var parts = new List<Wrc9DamagePart>();
            pending.Push(Pointer(head + 8));
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node == head) continue;
                if (node == 0 || !seen.Add(node) || (ulong)seen.Count > count) return null;
                var entry = read(node, 48); var part = BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(40));
                var values = read(part, 24); var nameAddress = BinaryPrimitives.ReadUInt64LittleEndian(values);
                if (nameAddress != BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(32))) return null;
                var text = read(nameAddress, 64); var end = Array.IndexOf(text, (byte)0);
                if (end is < 1 or > 63 || text.Take(end).Any(b => b < 32 || b > 126)) return null;
                var name = Encoding.ASCII.GetString(text, 0, end);
                if (!names.Add(name)) return null;
                var maximum = Wrc9VehicleTelemetry.Single(values, 8); var lost = Wrc9VehicleTelemetry.Single(values, 12);
                var warning = Wrc9VehicleTelemetry.Single(values, 16); var critical = Wrc9VehicleTelemetry.Single(values, 20);
                if (!float.IsFinite(maximum) || maximum is <= 0 or > 100000 || !float.IsFinite(lost) || lost < 0 || lost > maximum ||
                    !float.IsFinite(warning) || warning is < 0 or > 1 || !float.IsFinite(critical) || critical < 0 || critical > warning) return null;
                parts.Add(new(name, maximum, lost, warning, critical));
                pending.Push(BinaryPrimitives.ReadUInt64LittleEndian(entry));
                pending.Push(BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(16)));
                if (Pointer(node + 0x28) != part || Pointer(part) != nameAddress) return null;
            }
            if ((ulong)seen.Count != count || Pointer(participant + 0xc78) != owner || Pointer(owner + 0xd0) != head ||
                Pointer(owner + 0xd8) != count || Pointer(owner + 0x40) != vehicle || Pointer(owner + 0xf8) != participant) return null;
            return Create(parts);
        }
        catch (IOException) { return null; }
        catch (OverflowException) { return null; }
    }

    internal static CarDamageSnapshot Create(IReadOnlyList<Wrc9DamagePart> parts)
    {
        var components = new List<CarDamageComponentSnapshot>();
        foreach (var part in parts)
        {
            var (kind, location) = Describe(part.Name);
            if (kind is null) continue;
            components.Add(new(CarDamageIds.ComponentId(kind, location), kind, location, true,
                part.Severity > 0, part.Severity, part.Magnitude));
        }
        var groups = components.GroupBy(c => c.Kind).Select(g => new CarDamageGroupSnapshot(g.Key, true,
            g.Count() == (g.Key is CarDamageIds.Brake or CarDamageIds.Suspension ? 4 : g.Key == "steering" ? 2 : 1),
            g.Max(c => c.Severity), g.Where(c => c.IsActive).Select(c => c.Id).ToArray())).ToList();
        // Corner summaries include the available mechanical parts; tyre/wheel
        // condition is not established by the mechanical damage table.
        groups.AddRange(components.Where(c => c.Location.Length > 0).GroupBy(c => c.Location)
            .Select(g => new CarDamageGroupSnapshot(g.Key, true, false, g.Max(c => c.Severity),
                g.Where(c => c.IsActive).Select(c => c.Id).ToArray())));
        return CarDamageSnapshot.Create("wrc9", DateTimeOffset.UtcNow, groups, components,
            parts.Select(p => new CarDamageRawComponentSnapshot(p.Name, "wrc9", p.Name, true, p.Severity > 0,
                p.Severity, p.Magnitude, p.Lost, null, [p.Maximum, p.Lost, p.WarningHealth, p.CriticalHealth], [], string.Empty)));
    }

    private static (string? Kind, string Location) Describe(string name)
    {
        if (name == "Engine") return (CarDamageIds.Engine, "");
        if (name == "Gearbox") return (CarDamageIds.Gearbox, "");
        if (name == "Body") return (CarDamageIds.Bodywork, "");
        // Keep independently named components even when current dashboard
        // artwork has no dedicated turbo/steering indicator.
        if (name == "Turbo") return ("turbo", "");
        var split = name.Split('_');
        if (split.Length != 2) return (null, "");
        var location = split[1] switch { "FL" => CarDamageIds.FrontLeft, "FR" => CarDamageIds.FrontRight,
            "RL" => CarDamageIds.RearLeft, "RR" => CarDamageIds.RearRight, _ => "" };
        if (location.Length == 0) return (null, "");
        return (split[0] switch { "Brake" => CarDamageIds.Brake, "Susp" => CarDamageIds.Suspension, "Steering" => "steering", _ => null }, location);
    }
}

internal sealed class Wrc9DamageEvents
{
    private CarDamageSnapshot? previous;
    private string? identity;
    private float time;

    internal IReadOnlyList<CarDamageEvent> Update(Wrc9Snapshot? sample, GameState state, bool newAttempt)
    {
        var current = sample?.Telemetry.CarDamage;
        var elapsed = sample?.RawMilliseconds / 1000f ?? 0;
        var before = previous;
        var comparable = !newAttempt && identity == sample?.Identity && state == GameState.Racing &&
            before is not null && current is not null && elapsed > time && elapsed - time <= .5f;
        previous = state == GameState.Racing ? current : null; identity = sample?.Identity; time = elapsed;
        if (!comparable) return [];
        var old = before!.RawComponents.ToDictionary(p => p.SourceName);
        // Repairs/restarts establish a baseline; never announce old damage on attachment.
        if (current!.RawComponents.Any(p => old.TryGetValue(p.SourceName, out var p0) && p.Magnitude < p0.Magnitude)) return [];
        var events = new List<CarDamageEvent>();
        foreach (var part in current.RawComponents)
        {
            if (!old.TryGetValue(part.SourceName, out var p0)) continue;
            if (part.SourceName == "Engine" && part.Severity > p0.Severity)
                events.Add(new() { DamageType = CarDamage.Engine, Parameters = new() { [CarDamageConstants.SEVERITY] = part.Severity } });
            // A single tick losing >=1% body life is a collision, rather than
            // ordinary accumulated wear. Severity bands are our announcement policy.
            if (part.SourceName == "Body" && part.Magnitude - p0.Magnitude is float loss && loss >= .01f)
                events.Add(new() { DamageType = CarDamage.Collision, Parameters = new()
                {
                    [CarDamageConstants.SEVERITY] = loss >= .15f ? 2 : loss >= .05f ? 1 : 0,
                    [CarDamageConstants.PART] = "Body", [CarDamageConstants.VALUE] = part.Magnitude!.Value,
                    [CarDamageConstants.PREVIOUS_VALUE] = p0.Magnitude!.Value
                } });
        }
        return events;
    }
}
