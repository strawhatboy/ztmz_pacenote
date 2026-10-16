using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc7DamageMemoryTests
{
    private sealed class Memory
    {
        internal const ulong Image = 0x140000000, Participant = 0x10000, Vehicle = 0x20000,
            Owner = 0x40000, Head = 0x50000, Node = 0x60000, Part = 0x70000, Name = 0x80000;
        private readonly Dictionary<ulong, byte> bytes = new();
        internal void Put(ulong a, byte[] b) { for (var i = 0; i < b.Length; i++) bytes[a + (ulong)i] = b[i]; }
        internal void Pointer(ulong a, ulong v) => Put(a, BitConverter.GetBytes(v));
        internal void Float(ulong a, float v) => Put(a, BitConverter.GetBytes(v));
        internal byte[] Read(ulong a, int n) => Enumerable.Range(0, n).Select(i => bytes.TryGetValue(a + (ulong)i, out var b) ? b : throw new IOException("Unavailable")).ToArray();
        internal Memory()
        {
            Pointer(Participant + 0xb80, Owner); Pointer(Owner, Image + 0xdba450); Pointer(Owner + 0x40, Vehicle); Pointer(Owner + 0x280, Participant); Pointer(Owner + 0x288, Vehicle);
            Pointer(Owner + 0x100, Head); Pointer(Owner + 0x108, 1); Pointer(Head + 8, Node);
            Put(Node, new byte[48]); Pointer(Node, Head); Pointer(Node + 16, Head); Pointer(Node + 32, Name); Pointer(Node + 40, Part);
            Pointer(Part, Name); Float(Part + 8, 100); Float(Part + 12, 35); Float(Part + 16, 10);
            Put(Name, new byte[64]); Put(Name, Encoding.ASCII.GetBytes("Engine"));
        }
        internal CarDamageSnapshot? Damage(Func<ulong, int, byte[]>? read = null) => Wrc7Damage.Read(read ?? Read, Image, Participant, Vehicle);
    }

    [Fact]
    public void VerifiedTableIncludesImpactAndWearAndCapsAtFullDamage()
    {
        var m = new Memory(); var part = Assert.Single(m.Damage()!.Components);
        Assert.Equal(CarDamageIds.Engine, part.Kind); Assert.Equal(.45f, part.Magnitude);
        m.Float(Memory.Part + 12, 120);
        Assert.Equal(1, Assert.Single(m.Damage()!.Components).Magnitude);
    }

    [Theory]
    [InlineData("cycle")] [InlineData("count")] [InlineData("count-mismatch")]
    [InlineData("owner")] [InlineData("reciprocal")] [InlineData("vtable")]
    [InlineData("name")] [InlineData("unterminated")] [InlineData("nan")]
    [InlineData("negative")] [InlineData("wear")] [InlineData("maximum")]
    public void InvalidTablesAreBoundedAndUnavailable(string fault)
    {
        var m = new Memory();
        switch (fault)
        {
            case "cycle": m.Pointer(Memory.Node, Memory.Node); break;
            case "count": m.Pointer(Memory.Owner + 0x108, 65); break;
            case "count-mismatch": m.Pointer(Memory.Owner + 0x108, 2); break;
            case "owner": m.Pointer(Memory.Owner + 0x40, 0); break;
            case "reciprocal": m.Pointer(Memory.Owner + 0x288, 0); break;
            case "vtable": m.Pointer(Memory.Owner, 0); break;
            case "name": m.Pointer(Memory.Node + 32, 0); break;
            case "unterminated": m.Put(Memory.Name, Enumerable.Repeat((byte)'A', 64).ToArray()); break;
            case "nan": m.Float(Memory.Part + 12, float.NaN); break;
            case "negative": m.Float(Memory.Part + 12, -1); break;
            case "wear": m.Float(Memory.Part + 16, float.PositiveInfinity); break;
            case "maximum": m.Float(Memory.Part + 8, 0); break;
        }
        var reads = 0;
        byte[] Read(ulong a, int n) { Assert.True(++reads < 100); return m.Read(a, n); }
        Assert.Null(m.Damage(Read));
    }

    [Theory]
    [InlineData(0xb80, true)] [InlineData(0x100, false)] [InlineData(0x108, false)]
    [InlineData(0x280, false)] [InlineData(0x288, false)]
    public void ChangedOwnershipOrTableDuringReadIsDiscarded(int offset, bool participant)
    {
        var m = new Memory(); var reads = 0;
        var address = (participant ? Memory.Participant : Memory.Owner) + (ulong)offset;
        byte[] Read(ulong a, int n) => a == address && ++reads == 2 ? BitConverter.GetBytes(0UL) : m.Read(a, n);
        Assert.Null(m.Damage(Read));
    }

    [Fact]
    public void UnreadableOptionalDamageIsUnavailable()
    {
        var m = new Memory();
        Assert.Null(m.Damage((_, _) => throw new IOException("Stage unloading")));
    }
}
