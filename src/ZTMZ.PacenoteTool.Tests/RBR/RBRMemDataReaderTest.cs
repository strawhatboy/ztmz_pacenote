using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.RBR;

namespace ZTMZ.PacenoteTool.Tests.RBR;

public class RBRMemDataReaderTest
{
    [Fact]
    public async Task CloseProgressAfterTargetProcessExitsDoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var processName = $"ztmzrmr{Guid.NewGuid():N}"[..15];
        var processPath = Path.Combine(Path.GetTempPath(), $"{processName}.exe");
        Process? process = null;

        try
        {
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), processPath);
            process = Process.Start(new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = "-t 127.0.0.1",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            Assert.NotNull(process);
            var reader = new RBRMemDataReader();
            Assert.True(reader.OpenProcess(new TestGame(processName)));

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();

            Assert.Null(Record.Exception(reader.CloseProgress));
            Assert.Null(Record.Exception(reader.CloseProgress));
        }
        finally
        {
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    await process.WaitForExitAsync();
                }
                finally
                {
                    process.Dispose();
                }
            }

            File.Delete(processPath);
        }
    }

    private sealed class TestGame(string executable) : IGame
    {
        public string WindowTitle => string.Empty;
        public string Name => "Test game";
        public string Description => string.Empty;
        public string ImageUri => string.Empty;
        public string Executable => executable;
        public IGamePacenoteReader GamePacenoteReader => null!;
        public IGameDataReader GameDataReader => null!;
        public IGamePrerequisiteChecker GamePrerequisiteChecker => null!;
        public Dictionary<string, IGameConfig> GameConfigurations { get; set; } = new();
        public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new();
        public event Action<int, string>? onCustomMessage;
        public bool IsRunning { get; set; }
        public bool IsInitialized { get; set; }
        public int Order => 0;

        public void OnCustomMessage(int level, string message)
        {
            onCustomMessage?.Invoke(level, message);
        }
    }
}
