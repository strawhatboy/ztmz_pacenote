using System.Text;
using Xunit;
using ZTMZ.PacenoteTool.Platform.Linux;

namespace ZTMZ.PacenoteTool.Portable.Tests;

public sealed class LinuxProcGameProcessSnapshotProviderTest
{
    [Fact]
    public void CaptureReadsProcessNameArgumentsAndSteamEnvironment()
    {
        var procRoot = Directory.CreateTempSubdirectory("ztmz-proc-test-");
        try
        {
            var processDirectory = procRoot.CreateSubdirectory("1234");
            File.WriteAllText(Path.Combine(processDirectory.FullName, "comm"), "wine64-preloader\n");
            File.WriteAllBytes(
                Path.Combine(processDirectory.FullName, "cmdline"),
                Encoding.UTF8.GetBytes("wine64-preloader\0Z:\\game\\drt.exe\0"));
            File.WriteAllBytes(
                Path.Combine(processDirectory.FullName, "environ"),
                Encoding.UTF8.GetBytes("SteamAppId=310560\0SteamGameId=310560\0"));

            var snapshot = Assert.Single(
                new LinuxProcGameProcessSnapshotProvider(procRoot.FullName).Capture());

            Assert.Equal(1234, snapshot.ProcessId);
            Assert.Equal("wine64-preloader", snapshot.ProcessName);
            Assert.Equal(["wine64-preloader", "Z:\\game\\drt.exe"], snapshot.Arguments);
            Assert.Equal("310560", snapshot.Environment["SteamAppId"]);
            Assert.Equal("310560", snapshot.Environment["SteamGameId"]);
        }
        finally
        {
            procRoot.Delete(recursive: true);
        }
    }
}
