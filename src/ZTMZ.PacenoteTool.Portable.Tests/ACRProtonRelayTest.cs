using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.AC;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class ACRProtonRelayTest
{
    [Fact]
    public async Task ProtocolReadsFragmentedFrame()
    {
        var payload = Encoding.UTF8.GetBytes("{\"token\":\"test\"}");
        var frame = CreateFrame(ACRRelayFrameType.Hello, payload);

        await using var stream = new FragmentedReadStream(frame, maximumReadSize: 2);
        var result = await ACRRelayProtocol.ReadFrameAsync(stream, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ACRRelayFrameType.Hello, result.Value.Type);
        Assert.Equal(payload, result.Value.Payload);
    }

    [Fact]
    public void ProtocolRejectsOversizedPayload()
    {
        var header = CreateHeader(ACRRelayFrameType.Telemetry, ACRRelayProtocol.MaximumPayloadSize + 1);

        Assert.False(ACRRelayProtocol.TryParseHeader(header, out _, out _));
    }

    [Fact]
    public void ProtocolTelemetrySizesMatchManagedSharedMemoryLayouts()
    {
        Assert.Equal(ACRRelayProtocol.GraphicsSize, Marshal.SizeOf<ACRGraphicsData>());
        Assert.Equal(ACRRelayProtocol.PhysicsSize, Marshal.SizeOf<ACRPhysicsData>());
        Assert.Equal(ACRRelayProtocol.StaticSize, Marshal.SizeOf<ACRStaticInfo>());
    }

    [Fact]
    public void LocatorFindsAcrExecutableCaseInsensitivelyAndProtonFromParent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ztmz-acr-proc-" + Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(root, "steamapps", "common", "Assetto Corsa Rally");
        var executable = Path.Combine(gameRoot, "ACR", "Binaries", "Win64", "ACR.exe");
        var protonRoot = Path.Combine(root, "steamapps", "common", "Proton Test");
        var proton = Path.Combine(protonRoot, "proton");
        var compatData = Path.Combine(root, "steamapps", "compatdata", "3917090");
        var process = Path.Combine(root, "4242");
        var protonProcess = Path.Combine(root, "4241");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            Directory.CreateDirectory(protonRoot);
            Directory.CreateDirectory(compatData);
            Directory.CreateDirectory(process);
            Directory.CreateDirectory(protonProcess);
            File.WriteAllText(executable, string.Empty);
            File.WriteAllText(proton, string.Empty);
            File.WriteAllText(Path.Combine(process, "comm"), "acr.exe\n");
            File.WriteAllBytes(Path.Combine(process, "cmdline"),
                Encoding.UTF8.GetBytes("Z:\\not-a-linux-path\\ACR.exe\0"));
            File.WriteAllBytes(Path.Combine(process, "environ"), Encoding.UTF8.GetBytes(string.Join('\0',
                "STEAM_COMPAT_APP_ID=3917090",
                $"STEAM_COMPAT_INSTALL_PATH={gameRoot}",
                $"STEAM_COMPAT_DATA_PATH={compatData}") + "\0"));
            File.WriteAllText(Path.Combine(process, "status"), "Name:\tacr.exe\nPPid:\t4241\n");
            File.WriteAllBytes(Path.Combine(protonProcess, "cmdline"),
                Encoding.UTF8.GetBytes("python3\0" + proton + "\0run\0"));
            File.WriteAllText(Path.Combine(protonProcess, "status"), "Name:\tpython3\nPPid:\t1\n");

            var result = ACRProtonProcessLocator.Find(root);

            Assert.NotNull(result);
            Assert.Equal(4242, result.ProcessId);
            Assert.Equal(executable, result.ExecutablePath);
            Assert.Equal(proton, result.ProtonPath);
            Assert.Equal(compatData, result.CompatDataPath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void LocatorRejectsAcrProcessFromAnotherSteamApplication()
    {
        var root = Path.Combine(Path.GetTempPath(), "ztmz-acr-proc-" + Guid.NewGuid().ToString("N"));
        var process = Path.Combine(root, "4242");
        try
        {
            Directory.CreateDirectory(process);
            File.WriteAllText(Path.Combine(process, "comm"), "acr.exe\n");
            File.WriteAllBytes(Path.Combine(process, "cmdline"), Encoding.UTF8.GetBytes("acr.exe\0"));
            File.WriteAllBytes(Path.Combine(process, "environ"),
                Encoding.UTF8.GetBytes("STEAM_COMPAT_APP_ID=123456\0"));

            Assert.Null(ACRProtonProcessLocator.Find(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AcrProviderExposesSteamIdentity()
    {
        var game = new ACRally();

        Assert.Equal((uint)3917090, ((ZTMZ.PacenoteTool.Base.Game.ISteamGameIdentity)game).SteamApplicationId);
    }

    private static byte[] CreateFrame(ACRRelayFrameType type, byte[] payload)
    {
        var frame = new byte[ACRRelayProtocol.HeaderSize + payload.Length];
        CreateHeader(type, payload.Length).CopyTo(frame, 0);
        payload.CopyTo(frame, ACRRelayProtocol.HeaderSize);
        return frame;
    }

    private static byte[] CreateHeader(ACRRelayFrameType type, int payloadLength)
    {
        var header = new byte[ACRRelayProtocol.HeaderSize];
        "ZAR1"u8.CopyTo(header);
        header[4] = ACRRelayProtocol.Version;
        header[5] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), payloadLength);
        return header;
    }

    private sealed class FragmentedReadStream(byte[] data, int maximumReadSize) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumReadSize)], cancellationToken);
        }
    }
}
