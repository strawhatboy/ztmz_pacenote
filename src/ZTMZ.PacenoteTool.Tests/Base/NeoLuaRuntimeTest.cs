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
}
