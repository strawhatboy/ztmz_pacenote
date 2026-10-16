using System.Buffers.Binary;
using System.Text;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

internal sealed record Wrc7DamagePart(string Name, float Maximum, float Impact, float Wear)
{
    // WRC7 0x3211f1: min(1, (impact + wear) / life).
    internal float Magnitude => Math.Min(1, (Impact + Wear) / Maximum);
    // ZTMZ display/announcement policy, not WRC7 native colour thresholds.
    internal int Severity => Magnitude >= .75f ? 2 : Magnitude >= .25f ? 1 : 0;
}

internal static class Wrc7Damage
{
    internal static CarDamageSnapshot? Read(Func<ulong, int, byte[]> read, ulong image, ulong participant, ulong vehicle)
    {
        try
        {
            ulong Pointer(ulong p) => BinaryPrimitives.ReadUInt64LittleEndian(read(p, 8));
            var owner = Pointer(participant + 0xb80);
            if (owner == 0 || Pointer(owner) != image + 0xdba450 || Pointer(owner + 0x40) != vehicle || Pointer(owner + 0x288) != vehicle || Pointer(owner + 0x280) != participant) return null;
            var head = Pointer(owner + 0x100); var count = Pointer(owner + 0x108);
            if (head == 0 || count is < 1 or > 64) return null;
            var seen = new HashSet<ulong>(); var names = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<ulong>(); var parts = new List<Wrc7DamagePart>();
            pending.Push(Pointer(head + 8));
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node == head) continue;
                if (node == 0 || !seen.Add(node) || (ulong)seen.Count > count) return null;
                var entry = read(node, 48); var part = BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(40));
                var values = read(part, 20); var nameAddress = BinaryPrimitives.ReadUInt64LittleEndian(values);
                if (nameAddress != BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(32))) return null;
                var text = read(nameAddress, 64); var end = Array.IndexOf(text, (byte)0);
                if (end is < 1 or > 63 || text.Take(end).Any(b => b < 32 || b > 126)) return null;
                var name = Encoding.ASCII.GetString(text, 0, end);
                if (!names.Add(name)) return null;
                var maximum = Wrc9VehicleTelemetry.Single(values, 8); var lost = Wrc9VehicleTelemetry.Single(values, 12);
                var wear = Wrc9VehicleTelemetry.Single(values, 16);
                // Native impact/wear accumulators can exceed maximum life; the
                // game's projection saturates their combined ratio at one.
                if (!float.IsFinite(maximum) || maximum is <= 0 or > 100000 ||
                    !float.IsFinite(lost) || lost is < 0 or > 100000000 ||
                    !float.IsFinite(wear) || wear is < 0 or > 100000000) return null;
                parts.Add(new(name, maximum, lost, wear));
                pending.Push(BinaryPrimitives.ReadUInt64LittleEndian(entry));
                pending.Push(BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(16)));
                if (Pointer(node + 0x28) != part || Pointer(part) != nameAddress) return null;
            }
            if ((ulong)seen.Count != count || Pointer(participant + 0xb80) != owner || Pointer(owner + 0x100) != head ||
                Pointer(owner + 0x108) != count || Pointer(owner + 0x40) != vehicle || Pointer(owner + 0x288) != vehicle || Pointer(owner + 0x280) != participant) return null;
            return Create(parts);
        }
        catch (IOException) { return null; }
        catch (OverflowException) { return null; }
    }

    internal static CarDamageSnapshot Create(IReadOnlyList<Wrc7DamagePart> parts)
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
            g.Count() == (g.Key is CarDamageIds.Wheel or CarDamageIds.Suspension ? 4 : 1),
            g.Max(c => c.Severity), g.Where(c => c.IsActive).Select(c => c.Id).ToArray())).ToList();
        // Brake damage is aggregate. No individual brake or puncture state is
        // inferred from mechanical wheel wear; corner coverage remains partial.
        groups.AddRange(components.Where(c => c.Location.Length > 0).GroupBy(c => c.Location)
            .Select(g => new CarDamageGroupSnapshot(g.Key, true, false, g.Max(c => c.Severity),
                g.Where(c => c.IsActive).Select(c => c.Id).ToArray())));
        return CarDamageSnapshot.Create("wrc7", DateTimeOffset.UtcNow, groups, components,
            parts.Select(p => new CarDamageRawComponentSnapshot(p.Name, "wrc7", p.Name, true, p.Severity > 0,
                p.Severity, p.Magnitude, p.Impact, null, [p.Maximum, p.Impact, p.Wear], [], string.Empty)));
    }

    private static (string? Kind, string Location) Describe(string name)
    {
        switch (name)
        {
            case "Engine": return (CarDamageIds.Engine, "");
            case "Gearbox": return (CarDamageIds.Gearbox, "");
            case "Brakes": return (CarDamageIds.Brake, "");
            case "Electronics": return ("electronics", "");
            case "Steering": return ("steering", "");
            // Preserve the actual parts; neither is a complete bodywork reading.
            case "FrontSplitter": return ("front-splitter", "");
            case "RearWing": return ("rear-wing", "");
        }
        var split = name.Split('_');
        if (split.Length != 2) return (null, "");
        var location = split[0] switch { "FL" => CarDamageIds.FrontLeft, "FR" => CarDamageIds.FrontRight,
            "RL" => CarDamageIds.RearLeft, "RR" => CarDamageIds.RearRight, _ => "" };
        if (location.Length == 0) return (null, "");
        return (split[1] switch { "Wheel" => CarDamageIds.Wheel, "Susp" => CarDamageIds.Suspension, _ => null }, location);
    }
}

internal sealed class Wrc7DamageEvents
{
    private CarDamageSnapshot? previous;
    private string? identity;
    private int time;

    internal IReadOnlyList<CarDamageEvent> Update(Wrc7Snapshot? sample, GameState state, bool newAttempt)
    {
        var current = sample?.Telemetry.CarDamage;
        var elapsed = sample?.RawMilliseconds ?? 0;
        var before = previous;
        var comparable = !newAttempt && identity == sample?.Identity && state == GameState.Racing &&
            before is not null && current is not null && elapsed > time && elapsed - time <= 500;
        previous = state == GameState.Racing ? current : null; identity = sample?.Identity; time = elapsed;
        if (!comparable) return [];
        var old = before!.RawComponents.ToDictionary(p => p.SourceName);
        // Any repair or table change establishes a fresh baseline. Compare the
        // independent impact accumulator too, including after total damage caps.
        if (current!.RawComponents.Count != old.Count || current.RawComponents.Any(p =>
            !old.TryGetValue(p.SourceName, out var p0) || p.FloatValues[0] != p0.FloatValues[0] ||
            p.FloatValues[1] < p0.FloatValues[1] || p.FloatValues[2] < p0.FloatValues[2])) return [];
        var events = new List<CarDamageEvent>();
        CarDamageRawComponentSnapshot? hit = null;
        var largestImpact = 0f;
        foreach (var part in current.RawComponents)
        {
            var p0 = old[part.SourceName];
            if (part.SourceName == "Engine" && part.Severity > p0.Severity)
                events.Add(new() { DamageType = CarDamage.Engine, Parameters = new() { [CarDamageConstants.SEVERITY] = part.Severity } });
            var impact = (part.FloatValues[1] - p0.FloatValues[1]) / part.FloatValues[0];
            if (impact > largestImpact) { largestImpact = impact; hit = part; }
        }
        // One collision can damage several parts. Announce only once, using the
        // largest new impact; gradual wear never contributes to this trigger.
        if (hit is not null && largestImpact >= .01f)
            events.Add(new() { DamageType = CarDamage.Collision, Parameters = new()
            {
                [CarDamageConstants.SEVERITY] = largestImpact >= .15f ? 2 : largestImpact >= .05f ? 1 : 0,
                [CarDamageConstants.PART] = hit.SourceName,
                [CarDamageConstants.VALUE] = hit.Magnitude!.Value,
                [CarDamageConstants.PREVIOUS_VALUE] = old[hit.SourceName].Magnitude!.Value
            } });
        return events;
    }
}
