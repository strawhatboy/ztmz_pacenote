using Neo.IronLua;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;

public class GlassCompactDashboardTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullRpmRangeFillsIndividuallyAndFlashesAtShiftPoint(bool web)
    {
        using var lua = new Lua(); var env = Load(lua);
        var data = new GameData { ShiftLightsRPMValid = true, ShiftLightsRPMStart = 8000, ShiftLightsRPMEnd = 9000, MaxRPM = 9000 };
        for (var count = 0; count <= 18; count++)
        {
            data.RPM = count == 0 ? 0 : (count - .5f) * 9000 / 18;
            Render(env, data, web);
            Assert.Equal(count, Convert.ToInt32(env["lit"]));
        }
        // The warning can start below the scale's maximum without rescaling the bar.
        data.ShiftLightsRPMStart = 6000; data.ShiftLightsRPMEnd = 7000;
        data.RPM = 4500; Render(env, data, web);
        Assert.Equal(9, Convert.ToInt32(env["lit"]));
        data.RPM = 7000; data.Time = 0;
        Render(env, data, web);
        Assert.Equal(18, Convert.ToInt32(env["red"]));
        data.Time = .125f;
        Render(env, data, web);
        Assert.Equal(0, Convert.ToInt32(env["lit"]));
        data.RPM = 4500;
        Render(env, data, web);
        Assert.Equal(9, Convert.ToInt32(env["lit"]));
    }

    [Fact]
    public void FallbackUsesConfiguredShiftPointAndInvalidRpmClearsBar()
    {
        using var lua = new Lua(); var env = Load(lua);
        var data = new GameData { MaxRPM = 8000, RPM = 800 };
        Render(env,data,true); Assert.Equal(2,Convert.ToInt32(env["lit"]));
        data.RPM = 4000;
        Render(env,data,true); Assert.Equal(9,Convert.ToInt32(env["lit"]));
        data.RPM = 7200;
        Render(env,data,true); Assert.Equal(18,Convert.ToInt32(env["red"]));
        foreach (var rpm in new[] { float.NaN, float.PositiveInfinity, -1, 0 })
        {
            data.RPM = rpm; Render(env,data,true);
            Assert.Equal(0,Convert.ToInt32(env["lit"]));
        }
        data.RPM = 4000;
        foreach (var maximum in new[] { float.NaN, float.PositiveInfinity, -1, 0 })
        {
            data.MaxRPM = maximum; Render(env,data,true);
            Assert.Equal(0,Convert.ToInt32(env["lit"]));
        }
    }

    [Theory]
    [InlineData(-1,"R")]
    [InlineData(0,"N")]
    [InlineData(3,"3")]
    [InlineData(10,"R")]
    public void GearAndStoppedSpeedUseActualTelemetry(int gear,string expected)
    {
        using var lua = new Lua(); var env = Load(lua);
        Assert.Equal($"{expected}|0|km/h",Render(env,new GameData {Gear=gear,Speed=0},true));
    }

    [Fact]
    public void InvalidTelemetryCannotLeavePreviousReadingsOnScreen()
    {
        using var lua = new Lua(); var env = Load(lua);
        Assert.Equal("3|108|km/h",Render(env,new GameData {Gear=3,Speed=108},true));
        Assert.Equal("—|—|km/h",Render(env,new GameData {Gear=99,Speed=float.NaN},true));
    }

    [Fact]
    public void GlassOffAndStandaloneUseSameAsymmetricGeometryWithoutGlassCommands()
    {
        using var lua = new Lua(); var env = Load(lua); var data = new GameData();
        Render(env,data,true);
        Assert.Equal(1,Convert.ToInt32(env["glassCalls"]));
        Assert.True(Convert.ToDouble(env["cornerRadius"]) > 0);
        var bounds = (string)env["panel"];
        Render(env,data,false);
        Assert.Equal(0,Convert.ToInt32(env["glassCalls"]));
        Assert.Equal(bounds,env["panel"]);
        env.CallMember("disableGlass"); Render(env,data,true);
        Assert.Equal(0,Convert.ToInt32(env["glassCalls"]));
        Assert.Equal(bounds,env["panel"]);
    }

    private static string Render(LuaTable env,GameData data,bool web) => (string)env.CallMember("render",data,web)[0];
    private static LuaTable Load(Lua lua)
    {
        var env=lua.CreateEnvironment();
        env.DoChunk(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestData/commonfunc.lua")),"common");
        env.DoChunk(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestData/glass-compact.lua")),"compact");
        env.DoChunk("""
            local function noop() end
            local function color(r,g,b,a) return {R=r,G=g,B=b,A=a or 255} end
            local function panelBounds(brush,x,y,right,bottom)
                assert(x>=0 and y>=0 and right<=1920 and bottom<=1080)
            end
            local function geometry()
                local points={}
                local function add(point)
                    assert(point.X>=0 and point.Y>=0 and point.X<=1920 and point.Y<=1080)
                    table.insert(points,point)
                end
                return {points=points,BeginFigure=add,AddPoint=add,EndFigure=noop,Close=noop,Dispose=noop}
            end
            local settings={}
            local texts={}
            local gfx={Width=1920,Height=1080,
                CreateSolidBrush=function(r,g,b,a) return {Color=color(r,g,b,a)} end,
                CreateFont=noop,CreateCustomFont=noop,DrawRoundedRectangle=noop,
                CreateGeometry=geometry,FillGeometry=noop,
                DrawGeometry=function(shape,brush,thickness)
                    assert(#shape.points==72)
                    if glassShape~=nil then
                        assert(#glassShape.points==8)
                        -- Rounded path stays inside every original convex edge.
                        for _,p in ipairs(shape.points) do
                            for i,a in ipairs(glassShape.points) do
                                local b=glassShape.points[i%8+1]
                                assert((b.X-a.X)*(p.Y-a.Y)-(b.Y-a.Y)*(p.X-a.X)>-.001)
                            end
                        end
                    end
                    local coords={}
                    for _,p in ipairs(shape.points) do table.insert(coords,p.X);table.insert(coords,p.Y) end
                    panel=table.concat(coords,',')
                end,
                FillRoundedRectangle=function(brush,x,y,right,bottom) panelBounds(brush,x,y,right,bottom) end,
                DrawGlassGeometry=function(shape,brush,refraction,blur,radius)
                    glassCalls=glassCalls+1;glassShape=shape;cornerRadius=radius
                end,
                DrawLine=function(brush,x,y,right,bottom,thickness)
                    local c=brush.Color
                    if c.A==255 then
                        lit=lit+1
                        if c.R==255 and c.G==69 then red=red+1 end
                    end
                end,
                drawTextWithBackgroundCentered=function(font,size,brush,background,x,y,value)
                    if brush.Color.A==255 then table.insert(texts,value) end
                end}
            local args={Graphics=gfx,Self={GetConfigByKey=function(key) return settings[key] end},
                GameOverlayDrawingHelper={getColor=color,getPoint=function(x,y) return {X=x,Y=y} end},
                Config={HudShiftGreenZone=.5,HudShiftRedZone=.9}}
            onInit(args)
            function disableGlass() settings['dashboards.settings.glassEnabled']=false end
            function render(data,web)
                texts={};lit=0;red=0;glassCalls=0;glassShape=nil
                args.GameData=data;args.IsWebDashboard=web
                onUpdate(args)
                return table.concat(texts,'|')
            end
            ""","graphics");
        return env;
    }
}
