using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.KT;

public sealed class Wrc9OptionalMemoryTests
{
    private sealed class Memory
    {
        internal const ulong Image = 0x140000000, Participant = 0x10000, Vehicle = 0x20000, Drive = 0x30000,
            Owner = 0x40000, Head = 0x50000, Node = 0x60000, Part = 0x70000, Name = 0x80000,
            Race = 0x90000, Timer = 0xa0000, Routes = 0xb0000, Reference = 0xc0000, Route = 0xd0000, Shape = 0xe0000;
        private readonly Dictionary<ulong, byte> bytes = new();
        internal void Put(ulong a, byte[] b) { for (var i = 0; i < b.Length; i++) bytes[a + (ulong)i] = b[i]; }
        internal void Pointer(ulong a, ulong v) => Put(a, BitConverter.GetBytes(v));
        internal void Int(ulong a, int v) => Put(a, BitConverter.GetBytes(v));
        internal void Float(ulong a, float v) => Put(a, BitConverter.GetBytes(v));
        internal byte[] Read(ulong a, int n) => Enumerable.Range(0, n).Select(i => bytes.TryGetValue(a + (ulong)i, out var b) ? b : throw new IOException("Unavailable")).ToArray();
        internal Memory()
        {
            Pointer(Participant + 0x70, Vehicle); Pointer(Vehicle, Image + 0x13feda8); Pointer(Vehicle + 0x60, Participant); Pointer(Vehicle + 0x80, Drive);
            Put(Drive, new byte[0x538]); Float(Drive + 0xe0, .75f); Float(Drive + 0xf0, 1); Float(Drive + 0x448, 840);
            Int(Drive + 0x128, 2); Int(Drive + 0x534, 8);
            Pointer(Participant + 0xc78, Owner); Pointer(Owner, Image + 0x1401488); Pointer(Owner + 0x40, Vehicle); Pointer(Owner + 0xf8, Participant);
            Pointer(Owner + 0xd0, Head); Pointer(Owner + 0xd8, 1); Pointer(Head + 8, Node);
            Put(Node, new byte[48]); Pointer(Node, Head); Pointer(Node + 16, Head); Pointer(Node + 32, Name); Pointer(Node + 40, Part);
            Pointer(Part, Name); Float(Part + 8, 100); Float(Part + 12, 35); Float(Part + 16, .75f); Float(Part + 20, .25f);
            Put(Name, new byte[64]); Put(Name, Encoding.ASCII.GetBytes("Body"));
            Pointer(Timer, Image + 0x13f6f98); Int(Race + 0x808, 1); Int(Race + 0x844, 1); Int(Timer + 0x70, 0);
            Pointer(Race + 0x848, Routes); Pointer(Routes + 8, Reference); Pointer(Reference + 0x38, Route); Pointer(Route + 0x178, Shape);
            Float(Shape + 0x8c, 7112.941f); Float(Timer + 0x88, 2684.9316f);
        }
        internal CarDamageSnapshot? Damage(Func<ulong, int, byte[]>? read = null) => Wrc9Damage.Read(read ?? Read, Image, Participant, Vehicle);
        internal GameData VehicleData(Func<ulong, int, byte[]>? read = null) => Wrc9SessionMemory.ReadVehicle(read ?? Read, Image, 0, Participant, 12);
        internal GameData Progress(Func<ulong, int, byte[]>? read = null) => Wrc9SessionMemory.ReadProgress(read ?? Read, Image, Race, Timer, new() { LapTime = 12, Speed = 54 });
    }

    [Fact]
    public void OptionalUnreadableWheelsAndPhysicsDoNotDiscardControlsOrDamage()
    {
        var memory = new Memory(); var data = memory.VehicleData();
        Assert.Equal(12, data.LapTime); Assert.Equal(.75f, data.Throttle); Assert.Equal(-1, data.Steering);
        Assert.Equal(1, data.Gear); Assert.Equal(6, data.MaxGears); Assert.Equal(.35f, Assert.Single(data.CarDamage.Components).Magnitude);
        memory.Pointer(Memory.Participant + 0xc78, 0);
        data = memory.VehicleData(); Assert.Null(data.CarDamage); Assert.Equal(.75f, data.Throttle);
    }

    [Theory]
    [InlineData("cycle")] [InlineData("count")] [InlineData("owner")] [InlineData("name")]
    [InlineData("nan")] [InlineData("negative")] [InlineData("threshold")]
    public void CorruptDamageTablesAreBoundedAndUnavailable(string fault)
    {
        var m = new Memory();
        switch (fault)
        {
            case "cycle": m.Pointer(Memory.Node, Memory.Node); break;
            case "count": m.Pointer(Memory.Owner + 0xd8, 65); break;
            case "owner": m.Pointer(Memory.Owner + 0x40, 0); break;
            case "name": m.Pointer(Memory.Node + 32, 0); break;
            case "nan": m.Float(Memory.Part + 12, float.NaN); break;
            case "negative": m.Float(Memory.Part + 12, -1); break;
            case "threshold": m.Float(Memory.Part + 20, .9f); break;
        }
        Assert.Null(m.Damage()); Assert.Equal(.75f, m.VehicleData().Throttle);
    }

    [Fact]
    public void ChangedDamageOwnerAndChangedVehicleDiscardStaleValues()
    {
        var m = new Memory(); var count = 0;
        byte[] TornDamage(ulong a, int n) => a == Memory.Participant + 0xc78 && ++count == 2 ? BitConverter.GetBytes(0UL) : m.Read(a, n);
        Assert.Null(m.Damage(TornDamage)); count = 0;
        byte[] TornVehicle(ulong a, int n) => a == Memory.Participant + 0x70 && ++count == 2 ? BitConverter.GetBytes(0UL) : m.Read(a, n);
        var data = m.VehicleData(TornVehicle); Assert.Equal(12, data.LapTime); Assert.Equal(0, data.Throttle); Assert.Null(data.CarDamage);
        count = 0;
        byte[] UnreadableOwner(ulong a, int n) => a == Memory.Participant + 0x70 && ++count == 2 ? throw new IOException() : m.Read(a, n);
        Assert.Equal(0, m.VehicleData(UnreadableOwner).Throttle);
    }

    [Fact]
    public void ProgressUsesRouteDistanceAndCanDecreaseWithoutOdometerIntegration()
    {
        var m = new Memory(); var data = m.Progress();
        Assert.Equal(2684.9316f, data.LapDistance); Assert.Equal(7112.941f, data.TrackLength);
        Assert.Equal(.3774714f, data.CompletionRate, 6); Assert.Equal(54, data.Speed); Assert.Equal(12, data.LapTime);
        m.Float(Memory.Timer + 0x88, 2600); Assert.Equal(2600, m.Progress().LapDistance);
        m.Float(Memory.Timer + 0x88, 0); Assert.Equal(0, m.Progress().CompletionRate);
        m.Float(Memory.Timer + 0x88, 7112.941f); Assert.Equal(1, m.Progress().CompletionRate);
    }

    [Theory]
    [InlineData("index")] [InlineData("count")] [InlineData("laps")] [InlineData("nan")] [InlineData("distance")] [InlineData("missing")]
    public void MissingOrUnverifiedRouteKeepsOtherTelemetry(string fault)
    {
        var m = new Memory();
        switch (fault)
        {
            case "index": m.Int(Memory.Timer + 0x70, -1); break;
            case "count": m.Int(Memory.Race + 0x844, 129); break;
            case "laps": m.Int(Memory.Race + 0x808, 2); break;
            case "nan": m.Float(Memory.Shape + 0x8c, float.NaN); break;
            case "distance": m.Float(Memory.Timer + 0x88, 100000); break;
            case "missing": m.Pointer(Memory.Race + 0x848, 0); break;
        }
        var data = m.Progress(); Assert.Equal(0, data.TrackLength); Assert.Equal(12, data.LapTime); Assert.Equal(54, data.Speed);
    }

    [Fact]
    public void ChangedRouteDuringReadIsDiscarded()
    {
        var m = new Memory(); var count = 0;
        byte[] Read(ulong a, int n) => a == Memory.Route + 0x178 && ++count == 2 ? BitConverter.GetBytes(0UL) : m.Read(a, n);
        Assert.Equal(0, m.Progress(Read).TrackLength);
    }
}
