using System.IO.Compression;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class SupportBundleWriterTest
{
    [Fact]
    public void CreatesBundleWithDiagnosticsAndLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZTMZSupportBundleTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logFile = Path.Combine(root, "current.log");
            File.WriteAllText(logFile, "recent diagnostic log");

            var result = new SupportBundleWriter().Create(
                Path.Combine(root, "support"),
                "{ \"status\": \"ok\" }",
                new[] { logFile });

            Assert.True(File.Exists(result.BundlePath));
            Assert.Contains("diagnostics.json", result.Entries);
            Assert.Contains("logs/current.log", result.Entries);

            using var archive = ZipFile.OpenRead(result.BundlePath);
            var diagnosticsEntry = archive.GetEntry("diagnostics.json");
            var logEntry = archive.GetEntry("logs/current.log");

            Assert.NotNull(diagnosticsEntry);
            Assert.NotNull(logEntry);

            using var diagnosticsReader = new StreamReader(diagnosticsEntry!.Open());
            using var logReader = new StreamReader(logEntry!.Open());
            Assert.Contains("\"status\": \"ok\"", diagnosticsReader.ReadToEnd());
            Assert.Equal("recent diagnostic log", logReader.ReadToEnd());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
