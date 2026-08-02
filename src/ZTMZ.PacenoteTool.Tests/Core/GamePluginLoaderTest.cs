using System;
using System.IO;
using NLog;
using Xunit;
using ZTMZ.PacenoteTool.Core;
using RBRGame = ZTMZ.PacenoteTool.RBR.RBR;

namespace ZTMZ.PacenoteTool.Tests.Core;

public class GamePluginLoaderTest
{
    [Fact]
    public void NativeLibraryDoesNotPreventManagedGameProviderLoading()
    {
        var nativeLibraryPath = Path.Combine(AppContext.BaseDirectory, "openvr_api.dll");

        Assert.True(File.Exists(nativeLibraryPath));

        var games = GamePluginLoader.Load(
            new[] { nativeLibraryPath, typeof(RBRGame).Assembly.Location },
            LogManager.GetCurrentClassLogger());

        Assert.Contains(games, game => game is RBRGame);
    }
}
