using Neo.IronLua;
using ZTMZ.PacenoteTool.Base.Game;
using ZTMZ.PacenoteTool.Base.UI;

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
            Assert.All(expiredImages, image => Assert.Equal(0.24f, image.Opacity.GetValueOrDefault()));
            Assert.Contains(
                expiredCommands.OfType<FillRoundedRectangleCommand>(),
                command => command.Color == 0x48A67A2Du);

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
            Assert.Contains(
                expiredCriticalCommands.OfType<FillRoundedRectangleCommand>(),
                command => command.Color == 0x52913F3Au);

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
