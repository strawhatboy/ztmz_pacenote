using Neo.IronLua;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.UI.Windows;
using ZTMZ.PacenoteTool.WebDashboard;

namespace ZTMZ.PacenoteTool.Tests.Base;

public class NeoLuaRuntimeTest
{
    [Fact]
    public void CanCreateLuaEnvironment()
    {
        using var lua = new Lua();

        var environment = lua.CreateEnvironment();

        Assert.NotNull(environment);
    }

    [Fact]
    public void LuaCanReadSharedCarDamageDashboardState()
    {
        using var lua = new Lua();
        var environment = lua.CreateEnvironment();
        environment.DefaultCompileOptions = new LuaCompileOptions { ClrEnabled = false };
        environment.DoChunk(
            "function verify(args) " +
            "local damage = args.GameData.CarDamage; " +
            "if damage == nil or damage.Dashboard == nil then error('damage unavailable') end; " +
            "if damage.Dashboard.EngineSeverity ~= 3 then error('engine severity') end; " +
            "if damage.Dashboard.FrontLeftWheelSeverity ~= 2 then error('wheel severity') end " +
            "end",
            "car-damage-dashboard-test.lua");
        var damage = CarDamageSnapshot.Create(
            "test",
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            new[]
            {
                new CarDamageGroupSnapshot(CarDamageIds.Engine, true, true, 3, new[] { CarDamageIds.Engine }),
                new CarDamageGroupSnapshot(CarDamageIds.FrontLeft, true, true, 2, new[] { CarDamageIds.ComponentId(CarDamageIds.Wheel, CarDamageIds.FrontLeft) })
            },
            new[]
            {
                new CarDamageComponentSnapshot(CarDamageIds.Engine, CarDamageIds.Engine, string.Empty, true, true, 3, 0.75f),
                new CarDamageComponentSnapshot(CarDamageIds.ComponentId(CarDamageIds.Wheel, CarDamageIds.FrontLeft), CarDamageIds.Wheel, CarDamageIds.FrontLeft, true, true, 2, 0.5f)
            },
            Array.Empty<CarDamageRawComponentSnapshot>());

        environment.CallMember(
            "verify",
            new DashboardScriptArguments { GameData = new GameData { CarDamage = damage } });
    }

    [Fact]
    public void LuaCanDrawRoundedRectangleThroughTheBrowserCompatibleProxy()
    {
        using var lua = new Lua();
        var environment = lua.CreateEnvironment();
        environment.DefaultCompileOptions = new LuaCompileOptions { ClrEnabled = false };
        environment.DoChunk(
            "function render(args) " +
            "local brush = args.Graphics.CreateSolidBrush(239, 82, 71, 124); " +
            "args.Graphics.DrawRoundedRectangle(brush, 10, 20, 110, 120, 8, 2) " +
            "end",
            "rounded-rectangle-test.lua");
        var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };

        environment.CallMember(
            "render",
            new DashboardScriptArguments { Graphics = new GraphicsCommandProxy(capture) });

        var command = Assert.IsType<DrawRoundedRectangleCommand>(Assert.Single(capture.GetCommands()));
        Assert.Equal(8, command.Radius);
        Assert.Equal(2, command.Thickness);
    }

    [Fact]
    public void ShippedCarDamageDashboardScriptParsesAndInitializes()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "dashboards", "car_damage", "script.lua");
        Assert.True(File.Exists(scriptPath), $"Missing dashboard script: {scriptPath}");

        using var lua = new Lua();
        var environment = lua.CreateEnvironment();
        environment.DefaultCompileOptions = new LuaCompileOptions { ClrEnabled = false };
        environment.DoChunk(File.ReadAllText(scriptPath), scriptPath);

        environment.CallMember(
            "onInit",
            new DashboardScriptArguments
            {
                Graphics = new GraphicsCommandProxy(new DrawingCommandCapture { Width = 1920, Height = 1080 })
            });
        environment.CallMember("onExit");
    }

    [Fact]
    public void CarDamageDashboardHighlightsRaceStartAndDamageChangesForConfiguredDuration()
    {
        var dashboardSourceDirectory = Path.Combine(AppContext.BaseDirectory, "dashboards", "car_damage");
        var scriptPath = Path.Combine(dashboardSourceDirectory, "script.lua");
        var commonScriptPath = Path.Combine(AppContext.BaseDirectory, "dashboards", "common", "commonfunc.lua");
        var temporaryDashboardDirectory = Path.Combine(
            Path.GetTempPath(),
            "ZTMZPacenoteCarDamageDashboardTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDashboardDirectory);
        File.Copy(
            Path.Combine(dashboardSourceDirectory, "settings.json"),
            Path.Combine(temporaryDashboardDirectory, "settings.json"));

        try
        {
            var dashboard = new Dashboard(new DashboardDescriptor { Path = temporaryDashboardDirectory });
            dashboard.DashboardConfigurations["dashboards.carDamage.damageAlertDuration"] = 10.0f;

            var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
            var graphics = new GraphicsCommandProxy(capture);
            dashboard.ImageResources = CreateCarDamageImageResources(graphics, capture);

            using var lua = new Lua();
            var environment = lua.CreateEnvironment();
            environment.DefaultCompileOptions = new LuaCompileOptions { ClrEnabled = false };
            environment.DoChunk(File.ReadAllText(commonScriptPath), commonScriptPath);
            environment.DoChunk(File.ReadAllText(scriptPath), scriptPath);

            var arguments = new DashboardScriptArguments
            {
                Graphics = graphics,
                Self = dashboard,
                GameData = new GameData { Time = 0, CarDamage = CreateCarDamageSnapshot(2) },
                GameContext = new GameContext { GameState = (int)GameState.RaceBegin }
            };

            environment.CallMember("onInit", arguments);
            environment.CallMember("onUpdate", arguments);

            var startImages = capture.GetCommands().OfType<DrawImageCommand>().ToList();
            Assert.Equal(23, startImages.Count);
            Assert.All(startImages, image => Assert.Equal(0.78f, image.Opacity.GetValueOrDefault()));

            capture.ClearCommands();
            arguments.GameData = new GameData { Time = 10.1f, CarDamage = CreateCarDamageSnapshot(2) };
            arguments.GameContext = new GameContext { GameState = (int)GameState.Racing };
            environment.CallMember("onUpdate", arguments);

            var expiredCommands = capture.GetCommands();
            var expiredImages = expiredCommands.OfType<DrawImageCommand>().ToList();
            Assert.Equal(23, expiredImages.Count);
            var expiredEngineImage = Assert.Single(
                expiredImages,
                image => image.ImageKey == "images@engine_gearbox");
            Assert.Equal(0.64f, expiredEngineImage.Opacity.GetValueOrDefault());
            Assert.All(
                expiredImages.Where(image => image.ImageKey != "images@engine_gearbox"),
                image => Assert.Equal(0.24f, image.Opacity.GetValueOrDefault()));
            Assert.Contains(
                expiredCommands.OfType<FillRoundedRectangleCommand>(),
                command => command.Color == 0xBEEEAA26u);

            capture.ClearCommands();
            arguments.GameData = new GameData { Time = 12.0f, CarDamage = CreateCarDamageSnapshot(3) };
            environment.CallMember("onUpdate", arguments);

            var changedCommands = capture.GetCommands();
            var changedEngineImage = Assert.Single(
                changedCommands.OfType<DrawImageCommand>(),
                image => image.ImageKey == "images@engine_gearbox");
            Assert.Equal(1.0f, changedEngineImage.Opacity.GetValueOrDefault());

            capture.ClearCommands();
            arguments.GameData = new GameData { Time = 22.1f, CarDamage = CreateCarDamageSnapshot(3) };
            environment.CallMember("onUpdate", arguments);

            var expiredCriticalCommands = capture.GetCommands();
            var expiredCriticalEngineImage = Assert.Single(
                expiredCriticalCommands.OfType<DrawImageCommand>(),
                image => image.ImageKey == "images@engine_gearbox");
            Assert.Equal(0.72f, expiredCriticalEngineImage.Opacity.GetValueOrDefault());
            Assert.Contains(
                expiredCriticalCommands.OfType<FillRoundedRectangleCommand>(),
                command => command.Color == 0xCCE13C36u);

            environment.CallMember("onExit");
        }
        finally
        {
            Directory.Delete(temporaryDashboardDirectory, recursive: true);
        }
    }

    [Fact]
    public void PacenoteRoadbookDashboardRendersMultiTokenCallsAndFadedHistory()
    {
        var dashboardSourceDirectory = Path.Combine(AppContext.BaseDirectory, "dashboards", "pacenote_roadbook");
        var scriptPath = Path.Combine(dashboardSourceDirectory, "script.lua");
        var commonScriptPath = Path.Combine(AppContext.BaseDirectory, "dashboards", "common", "commonfunc.lua");
        Assert.True(File.Exists(scriptPath), $"Missing dashboard script: {scriptPath}");
        var previewPath = Path.Combine(dashboardSourceDirectory, "preview.png");
        Assert.True(File.Exists(previewPath), $"Missing dashboard preview: {previewPath}");
        using (var info = System.Text.Json.JsonDocument.Parse(
                   File.ReadAllText(Path.Combine(dashboardSourceDirectory, "info.json"))))
        {
            Assert.Equal(
                "preview.png",
                info.RootElement.GetProperty("PreviewImagePath").GetString());
        }

        var temporaryDashboardDirectory = Path.Combine(
            Path.GetTempPath(),
            "ZTMZPacenoteRoadbookDashboardTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDashboardDirectory);
        File.Copy(
            Path.Combine(dashboardSourceDirectory, "settings.json"),
            Path.Combine(temporaryDashboardDirectory, "settings.json"));

        try
        {
            var dashboard = new Dashboard(new DashboardDescriptor
            {
                Path = temporaryDashboardDirectory,
                UsesPacenoteIcons = true
            });
            Assert.Equal(0, Convert.ToInt32(dashboard.GetConfigByKey("dashboards.settings.positionH")));
            Assert.Equal(-1, Convert.ToInt32(dashboard.GetConfigByKey("dashboards.settings.positionV")));
            Assert.Equal(0.30, Convert.ToDouble(dashboard.GetConfigByKey("dashboards.settings.size")), 2);
            Assert.Equal(0, Convert.ToDouble(dashboard.GetConfigByKey("dashboards.settings.marginH")));
            Assert.Equal(0, Convert.ToDouble(dashboard.GetConfigByKey("dashboards.settings.marginV")));
            Assert.Equal(0.05, Convert.ToDouble(dashboard.GetConfigByKey("dashboards.settings.backgroundOpacity")), 2);
            Assert.True(Convert.ToBoolean(dashboard.GetConfigByKey("dashboards.pacenoteRoadbook.showLabels")));
            Assert.Equal(1, Convert.ToInt32(dashboard.GetConfigByKey("dashboards.pacenoteRoadbook.historyRows")));
            Assert.Equal(0.018, Convert.ToDouble(dashboard.GetConfigByKey("dashboards.pacenoteRoadbook.rowGap")), 3);
            Assert.Equal(0.02, Convert.ToDouble(dashboard.GetConfigByKey("dashboards.pacenoteRoadbook.iconLabelGap")), 2);
            dashboard.DashboardConfigurations["dashboards.pacenoteRoadbook.historyRows"] = 2;

            var capture = new DrawingCommandCapture { Width = 1920, Height = 1080 };
            var graphics = new GraphicsCommandProxy(capture);
            dashboard.ImageResources = new Dictionary<string, object>();
            foreach (var canonical in new[] { "into", "6_left", "dont_cut", "2_right" })
            {
                var key = "pacenote@" + canonical;
                var image = graphics.CreateImage(key, canonical + ".png");
                dashboard.ImageResources[key] = image;
                capture.RegisterImage(key, image, $"/pacenote-icons/v1/icons/{canonical}.png");
            }

            using var lua = new Lua();
            var environment = lua.CreateEnvironment();
            environment.DefaultCompileOptions = new LuaCompileOptions { ClrEnabled = false };
            environment.DoChunk(File.ReadAllText(commonScriptPath), commonScriptPath);
            environment.DoChunk(File.ReadAllText(scriptPath), scriptPath);

            var arguments = new DashboardScriptArguments
            {
                Graphics = graphics,
                Self = dashboard,
                GameData = new GameData { Time = 10 },
                GameContext = new GameContext
                {
                    CurrentPacenote = new PacenotePlaybackSnapshot(
                        100,
                        new[]
                        {
                            new PacenotePlaybackToken("detail_into", Array.Empty<string>()),
                            new PacenotePlaybackToken("corner_6_left", new[] { "detail_dont_cut" })
                        },
                        new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero),
                        "script",
                        "test.txt",
                        string.Empty)
                }
            };

            environment.CallMember("onInit", arguments);
            environment.CallMember("onUpdate", arguments);

            var firstCommands = capture.GetCommands();
            Assert.Equal(3, firstCommands.OfType<DrawImageCommand>().Count());
            Assert.Contains(firstCommands.OfType<DrawImageCommand>(), command => command.ImageKey == "pacenote@into");
            Assert.Contains(firstCommands.OfType<DrawImageCommand>(), command => command.ImageKey == "pacenote@6_left");
            Assert.Contains(firstCommands.OfType<DrawImageCommand>(), command => command.ImageKey == "pacenote@dont_cut");
            Assert.Equal(3, firstCommands.OfType<DrawTextCommand>().Count());
            Assert.Single(firstCommands.OfType<FillRoundedRectangleCommand>());

            dashboard.DashboardConfigurations["dashboards.pacenoteRoadbook.showLabels"] = false;
            environment.CallMember("onUpdate", arguments);
            Assert.Empty(capture.GetCommands().OfType<DrawTextCommand>());
            dashboard.DashboardConfigurations["dashboards.pacenoteRoadbook.showLabels"] = true;

            arguments.GameData = new GameData { Time = 11 };
            arguments.GameContext.CurrentPacenote = new PacenotePlaybackSnapshot(
                140,
                new[] { new PacenotePlaybackToken("corner_2_right", Array.Empty<string>()) },
                new DateTimeOffset(2026, 8, 17, 10, 0, 1, TimeSpan.Zero),
                "script",
                "test.txt",
                string.Empty);
            environment.CallMember("onUpdate", arguments);

            var secondImages = capture.GetCommands().OfType<DrawImageCommand>().ToList();
            Assert.Equal(1 + 3 * 12, secondImages.Count);
            Assert.Contains(secondImages, command => command.ImageKey == "pacenote@2_right" && command.Opacity == 1.0f);
            Assert.Contains(secondImages, command => command.ImageKey == "pacenote@6_left" && command.Opacity == 1.0f);

            arguments.GameData = new GameData { Time = 11.28f };
            capture.ClearCommands();
            environment.CallMember("onUpdate", arguments);
            var settledImages = capture.GetCommands().OfType<DrawImageCommand>().ToList();
            Assert.Contains(settledImages, command => command.ImageKey == "pacenote@2_right" && command.Opacity == 1.0f);
            var fadedHistorySlices = settledImages
                .Where(command => command.ImageKey == "pacenote@6_left")
                .Select(command => command.Opacity.GetValueOrDefault())
                .ToList();
            Assert.Equal(12, fadedHistorySlices.Count);
            Assert.True(fadedHistorySlices[0] < fadedHistorySlices[^1]);
            Assert.All(
                fadedHistorySlices.Zip(fadedHistorySlices.Skip(1)),
                pair => Assert.True(pair.First < pair.Second));

            arguments.GameContext.CurrentPacenote = PacenotePlaybackSnapshot.Empty;
            capture.ClearCommands();
            environment.CallMember("onUpdate", arguments);
            Assert.Empty(capture.GetCommands());
            environment.CallMember("onExit");
        }
        finally
        {
            Directory.Delete(temporaryDashboardDirectory, recursive: true);
        }
    }

    private static Dictionary<string, object> CreateCarDamageImageResources(
        GraphicsCommandProxy graphics,
        DrawingCommandCapture capture)
    {
        var imageResources = new Dictionary<string, object>();
        foreach (var imageName in new[]
                 {
                     "brake", "differential", "engine_gearbox", "front_bumper", "left_door",
                     "radiator_cooling", "rear_bumper", "right_door", "suspension", "tyre", "wheel"
                 })
        {
            var key = "images@" + imageName;
            var image = graphics.CreateImage(key, imageName + ".png");
            imageResources[key] = image;
            capture.RegisterImage(key, image);
        }

        return imageResources;
    }

    private static CarDamageSnapshot CreateCarDamageSnapshot(int engineSeverity)
    {
        return CarDamageSnapshot.Create(
            "test",
            DateTimeOffset.UtcNow,
            new[]
            {
                new CarDamageGroupSnapshot(
                    CarDamageIds.Engine,
                    true,
                    true,
                    engineSeverity,
                    new[] { CarDamageIds.Engine })
            },
            Array.Empty<CarDamageComponentSnapshot>(),
            Array.Empty<CarDamageRawComponentSnapshot>());
    }
}
