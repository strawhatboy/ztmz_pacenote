using System.Buffers.Binary;
using System.Text;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.BeamNG;

namespace ZTMZ.PacenoteTool.Tests.BeamNG;

public class BeamNGProtocolTest
{
    [Fact]
    public void TryParseOutGauge_MapsBeamNGTelemetryFields()
    {
        var packetBytes = new byte[BeamNGOutGaugePacket.MinimumPacketLength];
        Encoding.ASCII.GetBytes("beam").CopyTo(packetBytes.AsSpan(4, 4));
        packetBytes[10] = 4; // first=2, so this represents third gear.
        WriteSingle(packetBytes, 12, 22.5f);
        WriteSingle(packetBytes, 16, 6123f);
        WriteSingle(packetBytes, 20, 0.8f);
        WriteSingle(packetBytes, 24, 91.5f);
        WriteSingle(packetBytes, 28, 0.75f);
        WriteSingle(packetBytes, 36, 104f);
        BinaryPrimitives.WriteUInt32LittleEndian(packetBytes.AsSpan(44, 4), 1u << 2);
        WriteSingle(packetBytes, 48, 0.8f);
        WriteSingle(packetBytes, 52, 0.4f);
        WriteSingle(packetBytes, 56, 0.2f);

        var parsed = BeamNGOutGaugePacket.TryParse(packetBytes, out var packet);

        Assert.True(parsed);
        Assert.NotNull(packet);
        Assert.Equal("beam", packet.Car);
        Assert.Equal(3, packet.Gear);
        Assert.Equal(22.5f, packet.SpeedMetersPerSecond);
        Assert.Equal(6123f, packet.Rpm);
        Assert.Equal(0.8f, packet.Throttle);
        Assert.Equal(0.4f, packet.Brake);
        Assert.Equal(0.2f, packet.Clutch);
        Assert.Equal(1u << 2, packet.ShowLights);
    }

    [Fact]
    public void TryParseBridgePacket_ReadsStageAndPacenoteMetadata()
    {
        var packetBytes = Encoding.UTF8.GetBytes("""
            {
              "magic": "ZTMZ_BNG_RALLY",
              "protocolVersion": 1,
              "type": "stage",
              "status": "active",
              "vehicleName": "Hirochi Sunburst",
              "stage": {
                "id": "small_island-intro_rally_stage1",
                "name": "Intro Rally Stage",
                "isActive": true,
                "stageTimeSeconds": 93.4,
                "distanceMeters": 1842.7,
                "totalDistanceMeters": 5120.0,
                "completion": 0.36
              },
              "pacenote": {
                "id": "note-32",
                "name": "Left 4",
                "text": "Left 4 over crest",
                "distanceToMeters": 72.0
              }
            }
            """);

        var parsed = BeamNGRallyBridgePacket.TryParse(packetBytes, out var packet);

        Assert.True(parsed);
        Assert.NotNull(packet);
        Assert.Equal(BeamNGRallyBridgePacket.SupportedProtocolVersion, packet.ProtocolVersion);
        Assert.Equal("Hirochi Sunburst", packet.VehicleName);
        Assert.NotNull(packet.Stage);
        Assert.True(packet.Stage.IsActive);
        Assert.Equal(5120f, packet.Stage.TotalDistanceMeters);
        Assert.NotNull(packet.Pacenote);
        Assert.Equal("Left 4 over crest", packet.Pacenote.Text);
    }

    [Fact]
    public void TryParseBridgePacket_RejectsAnotherUdpPayload()
    {
        var packetBytes = Encoding.UTF8.GetBytes("""{"magic":"OTHER","protocolVersion":1}""");

        var parsed = BeamNGRallyBridgePacket.TryParse(packetBytes, out var packet);

        Assert.False(parsed);
        Assert.Null(packet);
    }

    [Fact]
    public void GetExportFileName_SanitizesStageIdentifier()
    {
        var fileName = BeamNGRallyStageStore.GetExportFileName("small/island:stage?1");

        Assert.Equal("small_island_stage_1.json", fileName);
    }

    [Fact]
    public void BeamNGRally_UsesPacenoteToolAudioByDefault()
    {
        var game = new BeamNGRally();

        Assert.DoesNotContain(
            typeof(IGamePacenotePlaybackControl),
            typeof(BeamNGRally).GetInterfaces());
        var udpConfig = Assert.IsType<UdpGameConfig>(game.DefaultGameConfigurations[UdpGameConfig.Name]);
        Assert.Equal(BeamNGGameDataReader.DefaultOutGaugePort, udpConfig.Port);
    }

    [Fact]
    public void RallySnapshot_ClearStageData_PreservesDashboardTelemetry()
    {
        var snapshot = new BeamNGRallySnapshot
        {
            StageId = "small-island-stage-1",
            StageName = "Small Island",
            StageActive = true,
            StageTimeSeconds = 42,
            DistanceMeters = 900,
            CurrentPacenoteText = "Right 4",
            FuelFraction = 0.75f,
            VehicleName = "Hirochi Sunburst"
        };

        snapshot.ClearStageData();

        Assert.Equal(string.Empty, snapshot.StageId);
        Assert.Equal(string.Empty, snapshot.CurrentPacenoteText);
        Assert.False(snapshot.StageActive);
        Assert.Equal(0f, snapshot.DistanceMeters);
        Assert.Equal(0.75f, snapshot.FuelFraction);
        Assert.Equal("Hirochi Sunburst", snapshot.VehicleName);
    }

    [Fact]
    public void BeamNGGameDataReader_UsesStableLabelsBeforeBridgeDataArrives()
    {
        var reader = new BeamNGGameDataReader();

        Assert.Equal("BeamNG.drive Rally", reader.TrackName);
        Assert.Equal("BeamNG.drive", reader.CarName);
    }

    private static void WriteSingle(byte[] target, int offset, float value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
    }
}
