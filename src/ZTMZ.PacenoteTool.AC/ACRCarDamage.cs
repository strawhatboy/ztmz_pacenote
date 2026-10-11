using System.Buffers.Binary;
using System.IO;
using System.Text;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

internal sealed record ACRDamagePart(string Name, float Life, float Maximum, int StateIndex, byte StateType);

// Offsets are gated by ACRVehicleMemory.SupportedHash. This is a read-only
// UCarDamageComponent snapshot, not the unrelated acpmf vehicleDamage array.
internal static class ACRCarDamage
{
    internal const ulong NamePoolRva = 0xb27ba80;
    internal const ulong ComponentVtableRva = 0x9c11ca8;

    internal static CarDamageSnapshot? Read(Func<ulong, int, byte[]> read, ulong image, ulong pawn)
    {
        ulong Pointer(ulong address)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(read(address, 8));
            if (value < 0x10000 || value > 0x7fffffffffff || (value & 7) != 0)
                throw new IOException("ACR damage pointer unavailable.");
            return value;
        }
        (ulong Address, int Count, byte[] Header) Array(ulong address, int limit)
        {
            var header = read(address, 16);
            var count = BitConverter.ToInt32(header, 8);
            var capacity = BitConverter.ToInt32(header, 12);
            if (count < 0 || capacity < count || capacity > limit) throw new IOException("Invalid ACR damage collection.");
            return (count == 0 ? 0 : Pointer(address), count, header);
        }
        string Name(byte[] name)
        {
            var index = BitConverter.ToUInt32(name);
            var number = BitConverter.ToUInt32(name, 4);
            if (index >> 16 >= 8192 || number > 10000) throw new IOException("Invalid ACR part name.");
            var block = Pointer(image + NamePoolRva + 0x10 + (index >> 16) * 8);
            var entry = block + (index & 65535) * 2;
            var header = BitConverter.ToUInt16(read(entry, 2));
            var length = header >> 6;
            if (length is < 1 or > 128) throw new IOException("Invalid ACR part name length.");
            var wide = (header & 1) != 0;
            var text = (wide ? Encoding.Unicode : Encoding.UTF8).GetString(read(entry + 2, length * (wide ? 2 : 1)));
            return number == 0 ? text : $"{text}_{number - 1}";
        }

        // Reflected AActor InstanceComponents and BlueprintCreatedComponents.
        // Do not scan the address space or invoke game functions.
        foreach (var offset in new ulong[] { 0x280, 0x290 })
        {
            var container = Array(pawn + offset, 1024);
            if (container.Count == 0) continue;
            var pointers = read(container.Address, container.Count * 8);
            for (var i = 0; i < container.Count; i++)
            {
                var component = BitConverter.ToUInt64(pointers, i * 8);
                if (component == 0) continue;
                if (Pointer(component) != image + ComponentVtableRva) continue;
                if (Pointer(component + 0xa0) != pawn || Pointer(component + 0x20) != pawn) return null;
                var parts = Array(component + 0x118, 128);
                if (parts.Count == 0) return null;
                var partPointers = read(parts.Address, parts.Count * 8);
                var result = new List<ACRDamagePart>(parts.Count);
                for (var j = 0; j < parts.Count; j++)
                {
                    var part = BitConverter.ToUInt64(partPointers, j * 8);
                    if (Pointer(part + 0x20) != component) return null;
                    var id = read(part + 0x58, 8);
                    var maximum = BitConverter.ToSingle(read(part + 0xb8, 4));
                    var current = read(part + 0x130, 8);
                    var state = BitConverter.ToInt32(current);
                    var life = BitConverter.ToSingle(current, 4);
                    var states = Array(part + 0xc0, 32);
                    if (!float.IsFinite(maximum) || maximum <= 0 || !float.IsFinite(life) || life < 0 || life > maximum ||
                        state < 0 || state >= states.Count) return null;
                    // State is an array index, NOT the enum. New and Normal
                    // both have Type=Normal. Each vehicle supplies its own thresholds.
                    var type = read(states.Address + (ulong)state * 0x20 + 8, 1)[0];
                    if (type is < 1 or > 4) return null;
                    var name = Name(id);
                    if (!read(part + 0x130, 8).AsSpan().SequenceEqual(current) ||
                        !read(part + 0xc0, 16).AsSpan().SequenceEqual(states.Header) ||
                        !read(part + 0x58, 8).AsSpan().SequenceEqual(id) || Pointer(part + 0x20) != component) return null;
                    result.Add(new(name, life, maximum, state, type));
                }
                if (!read(component + 0x118, 16).AsSpan().SequenceEqual(parts.Header) ||
                    !read(parts.Address, parts.Count * 8).AsSpan().SequenceEqual(partPointers) ||
                    !read(pawn + offset, 16).AsSpan().SequenceEqual(container.Header) ||
                    !read(container.Address, container.Count * 8).AsSpan().SequenceEqual(pointers) ||
                    Pointer(component + 0xa0) != pawn) return null;
                return Create(result);
            }
        }
        return null;
    }

    internal static CarDamageSnapshot Create(IEnumerable<ACRDamagePart> parts)
    {
        var components = new List<CarDamageComponentSnapshot>();
        var raw = new List<CarDamageRawComponentSnapshot>();
        foreach (var part in parts)
        {
            var severity = part.StateType == 1 ? 0 : part.StateType;
            var magnitude = Math.Clamp(1 - part.Life / part.Maximum, 0, 1);
            var (kind, location) = Map(part.Name);
            var id = CarDamageIds.ComponentId(kind, location);
            components.Add(new(id, kind, location, true, severity > 0, severity, magnitude));
            raw.Add(new(part.Name, "ACR", part.Name, true, severity > 0, severity, magnitude,
                part.Life, part.StateIndex, [part.Life, part.Maximum], [part.StateIndex, part.StateType],
                FormattableString.Invariant($"life={part.Life}/{part.Maximum}; stateIndex={part.StateIndex}; stateType={part.StateType}")));
        }
        var groups = components.Where(c => c.Location.Length != 0)
            .GroupBy(c => c.Kind == CarDamageIds.Differential ? CarDamageIds.Differential : c.Location)
            .Select(g => new CarDamageGroupSnapshot(g.Key, true, false, g.Max(c => c.Severity),
                g.Where(c => c.IsActive).Select(c => c.Id).ToArray()));
        return CarDamageSnapshot.Create("ACR", DateTimeOffset.UtcNow, groups, components, raw);
    }

    private static (string Kind, string Location) Map(string name)
    {
        foreach (var (suffix, location) in new[] { ("FrontLeft", CarDamageIds.FrontLeft), ("FrontRight", CarDamageIds.FrontRight),
                     ("RearLeft", CarDamageIds.RearLeft), ("RearRight", CarDamageIds.RearRight) })
        {
            if (name == "Suspension" + suffix) return (CarDamageIds.Suspension, location);
            if (name == "Brakes" + suffix) return (CarDamageIds.Brake, location);
            if (name == "WheelGeometry" + suffix) return (CarDamageIds.Wheel, location);
        }
        return name switch
        {
            "Engine" => (CarDamageIds.Engine, ""), "Radiator" => (CarDamageIds.Radiator, ""),
            "Bodywork" => (CarDamageIds.Bodywork, ""),
            "DifferentialFront" => (CarDamageIds.Differential, "front"),
            "DifferentialRear" => (CarDamageIds.Differential, "rear"),
            // Keep the remaining native components distinct. Do not label a
            // gearbox, windscreen or turbo failure as engine/tyre damage.
            _ => ("acr." + name.ToLowerInvariant(), "")
        };
    }
}
