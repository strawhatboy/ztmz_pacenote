using ZTMZ.PacenoteTool.Codemasters;
using Xunit;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class Dr2RaceStateMemoryTest
{
    private const ulong Image = 0x140000000, Players = 0x200000, PlayerList = 0x210000,
        Services = 0x220000, ServiceTable = 0x230000, Local = 0x240000,
        Player = 0x250000, Opponent = 0x260000, Race = 0x270000,
        Records = 0x280000, Record = 0x290000, OtherRecord = 0x300000;
    private const int Key = unchecked((int)0xbe1258ff);
    private readonly Dictionary<ulong, byte> memory = new();

    public Dr2RaceStateMemoryTest()
    {
        U64(Image + 0x16951e8, Players); I32(Players + 0x40, 2); U64(Players + 0x30, PlayerList);
        U64(Image + 0x1695150, Services); I32(Image + 0x16940c4, 7);
        U64(Services + 8, ServiceTable); U64(ServiceTable + 7 * 8, Local);
        Put(Local + 0x2a, BitConverter.GetBytes((ushort)12));
        U64(PlayerList, Opponent); U64(PlayerList + 8, Player);
        I32(Player + 0x2c, 3); Put(Player + 0xbc, BitConverter.GetBytes((ushort)12));
        I32(Opponent + 0x2c, 3); Put(Opponent + 0xbc, BitConverter.GetBytes((ushort)13));
        U64(Image + 0x1695240, Race); U64(Race + 0xacb0, 2); U64(Race + 0xaca0, Records);
        U64(Records, OtherRecord); U64(Records + 8, Record);
        U64(OtherRecord + 0x40, Opponent); U64(Record + 0x40, Player);
        I32(Image + 0x15d8510, Key);
        I32(OtherRecord + 0x18, Key ^ (int)Dr2RaceState.Retired);
        I32(Record + 0x18, Key ^ (int)Dr2RaceState.Racing);
    }

    [Theory]
    [InlineData(Dr2RaceState.Racing)]
    [InlineData(Dr2RaceState.Retired)]
    [InlineData(Dr2RaceState.TerminalDamage)]
    [InlineData(Dr2RaceState.Finished)]
    [InlineData(Dr2RaceState.None)]
    public void DecodesLocalPlayerAndIgnoresOpponent(Dr2RaceState state)
    {
        I32(Record + 0x18, Key ^ (int)state);
        Assert.Equal(new Dr2RaceStateSnapshot(Record, state), Dr2RaceStateMemory.Read(Read, Image));
    }

    [Fact]
    public void RejectsAmbiguousLocalPlayer()
    {
        Put(Opponent + 0xbc, BitConverter.GetBytes((ushort)12));
        Assert.Null(Dr2RaceStateMemory.Read(Read, Image));
    }

    [Fact]
    public void RejectsDuplicateResultRecords()
    {
        U64(OtherRecord + 0x40, Player);
        Assert.Null(Dr2RaceStateMemory.Read(Read, Image));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(int.MaxValue)]
    public void RejectsUnknownState(int value)
    {
        I32(Record + 0x18, Key ^ value);
        Assert.Null(Dr2RaceStateMemory.Read(Read, Image));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void BoundsPlayerCountBeforeFollowingEntries(int count)
    {
        I32(Players + 0x40, count);
        Assert.Null(Dr2RaceStateMemory.Read(Read, Image));
    }

    [Fact]
    public void RejectsOwnershipChangeDuringRead()
    {
        byte[] ChangingRead(ulong address, int count)
        {
            var bytes = Read(address, count);
            if (address == Record + 0x18) U64(Record + 0x40, Opponent);
            return bytes;
        }
        Assert.Null(Dr2RaceStateMemory.Read(ChangingRead, Image));
    }

    private void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
    private void I32(ulong address, int value) => Put(address, BitConverter.GetBytes(value));
    private void Put(ulong address, byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++) memory[address + (ulong)i] = bytes[i];
    }
    private byte[] Read(ulong address, int count) => Enumerable.Range(0, count).Select(i => memory[address + (ulong)i]).ToArray();
}
