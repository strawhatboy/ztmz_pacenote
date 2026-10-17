using Neo.IronLua;
using Xunit;
using ZTMZ.PacenoteTool.Base.Game;

public class LiquidGlassDashboardTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClrSnapshotsRenderAndDisappearWithoutStaleReadings(bool portable)
    {
        using var lua = new Lua();
        var env = Load(lua);
        var data = Sample();
        var text = (string)env.CallMember("render", data, portable)[0];
        Assert.Contains("94 °C", text);
        Assert.Contains("80%", text);
        Assert.Contains("1.24", text);
        Assert.Contains("TURBO / bar", text);
        Assert.Contains("COOLING", text);
        Assert.DoesNotContain("GEARBOX", text); // Unsupported component, not healthy.
        Assert.Equal(portable ? 2 : 0, Convert.ToInt32(env["glassCalls"]));
        data.Engine = null; data.Turbo = null; data.Hybrid = null; data.CarDamage = null;
        text = (string)env.CallMember("render", data, portable)[0];
        Assert.DoesNotContain("94 °C", text);
        Assert.DoesNotContain("80%", text);
        Assert.DoesNotContain("1.24", text);
        Assert.DoesNotContain("COOLING", text);
        Assert.Equal(portable ? 1 : 0, Convert.ToInt32(env["glassCalls"]));
    }

    [Theory]
    [InlineData("gauge", "TURBO / bar")]
    [InlineData("absolute", "INTAKE / bar abs")]
    [InlineData("unknown", "PRESSURE / bar")]
    public void PressureReferenceIsPreservedAndZeroIsAReading(string reference, string label)
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        data.Turbo = new("test", default) { PressureBar = 0, PressureReference = reference };
        var text = (string)env.CallMember("render", data, true)[0];
        Assert.Contains("0.00", text); Assert.Contains(label, text);
        if (reference != "gauge") Assert.DoesNotContain("TURBO / bar", text);
        data.Turbo = data.Turbo with { PressureBar = float.NaN };
        Assert.DoesNotContain(label, (string)env.CallMember("render", data, true)[0]);
    }

    [Fact]
    public void HandbrakeIsGreyReleasedRedAppliedAndHiddenWhenUnknown()
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        env.CallMember("render", data, true);
        Assert.Equal("151,169,185", env["handbrakeColor"]);
        data.HandBrake = 1;
        env.CallMember("render", data, true);
        Assert.Equal("255,69,86", env["handbrakeColor"]);
        data.HandBrakeValid = false;
        env.CallMember("render", data, true);
        Assert.Equal("", env["handbrakeColor"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptionalPowertrainColumnsCollapseAndReturnWithoutEmptySpace(bool portable)
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        var hybrid = data.Hybrid; var turbo = data.Turbo;
        var full = RenderBounds(env, data, portable);
        data.Hybrid = null;
        var turboOnly = RenderBounds(env, data, portable);
        data.Turbo = null;
        var neither = RenderBounds(env, data, portable);
        data.Hybrid = hybrid;
        var hybridOnly = RenderBounds(env, data, portable);
        Assert.True(full.Width > turboOnly.Width, "Missing hybrid must remove its column.");
        Assert.True(full.Width > hybridOnly.Width, "Missing turbo must remove its column.");
        Assert.True(turboOnly.Width > neither.Width, "Turbo alone must add a column.");
        Assert.True(hybridOnly.Width > neither.Width, "Hybrid alone must add a column.");
        data.Turbo = turbo;
        Assert.Equal(full.Width, RenderBounds(env, data, portable).Width, 5);

        SetSetting(env, "showHybrid", false);
        SetSetting(env, "showTurbo", false);
        Assert.Equal(neither.Width, RenderBounds(env, data, portable).Width, 5);
    }

    [Fact]
    public void InvalidOptionalReadingsDoNotReserveColumns()
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        data.Hybrid = null; data.Turbo = null;
        var empty = RenderBounds(env, data);
        data.Hybrid = new("test", default);
        data.Turbo = new("test", default) { PressureBar = float.NaN };
        Assert.Equal(empty.Width, RenderBounds(env, data).Width, 5);
        data.Turbo = data.Turbo with { PressureBar = 0 };
        Assert.True(RenderBounds(env, data).Width > empty.Width, "Valid zero pressure still needs its column.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AbsentTemperaturesDoNotLeaveAPlaceholderRow(bool portable)
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        data.CarDamage = null;
        var withTemperature = RenderBounds(env, data, portable);
        data.Engine = null;
        var withoutTemperature = RenderBounds(env, data, portable);
        Assert.True(withTemperature.Height > withoutTemperature.Height,
            "An absent temperature/delta row must reduce the main panel height.");
        data.Engine = new("test", default);
        Assert.Equal(withoutTemperature.Height, RenderBounds(env, data, portable).Height, 5);
        data.Engine = data.Engine with { CoolantTemperatureCelsius = float.NaN, OilTemperatureCelsius = float.PositiveInfinity };
        Assert.Equal(withoutTemperature.Height, RenderBounds(env, data, portable).Height, 5);
        data.Engine = data.Engine with { CoolantTemperatureCelsius = 94, OilTemperatureCelsius = null };
        SetSetting(env, "showTemperatures", false);
        Assert.Equal(withoutTemperature.Height, RenderBounds(env, data, portable).Height, 5);
    }

    [Fact]
    public void OilPressureWarningRetainsRowEvenWithoutTemperatureReadings()
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        data.CarDamage = null; data.Engine = null;
        var empty = RenderBounds(env, data);
        data.Engine = new("test", default) { OilPressureWarning = true };
        Assert.True(RenderBounds(env, data).Height > empty.Height);
        Assert.Contains("OIL PRESSURE", (string)env.CallMember("render", data, true)[0]);
    }

    [Theory]
    [InlineData(true, true, "COOLANT")]
    [InlineData(false, true, "COOLANT")]
    [InlineData(true, false, "ENGINE")]
    [InlineData(false, false, "ENGINE")]
    public void WarningOnlySnapshotShowsRedIconAndCollapsesWhenCleared(bool portable, bool coolant, string label)
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        data.CarDamage = null; data.Engine = null;
        var empty = RenderBounds(env, data, portable);
        data.Engine = new("test", default)
        {
            CoolantTemperatureWarning = coolant ? true : null,
            EngineWarning = coolant ? null : true
        };
        Assert.True(RenderBounds(env, data, portable).Height > empty.Height);
        var text = (string)env.CallMember("render", data, portable)[0];
        Assert.Contains(label, text);
        Assert.DoesNotContain("°C", text);
        Assert.True(Convert.ToInt32(env["redIconLines"]) > 0, "The warning icon must be red.");
        data.Engine = data.Engine with { CoolantTemperatureWarning = false, EngineWarning = false };
        Assert.Equal(empty.Height, RenderBounds(env, data, portable).Height, 5);
        Assert.DoesNotContain(label, (string)env.CallMember("render", data, portable)[0]);
        Assert.Equal(0, Convert.ToInt32(env["redIconLines"]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FourthReadingWrapsToAnotherRowAndRowDisappearsWithDelta(bool portable)
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        data.CarDamage = null;
        data.Engine = new("test", default)
        { CoolantTemperatureWarning = true, OilPressureWarning = true, EngineWarning = true };
        var three = RenderBounds(env, data, portable);
        env.CallMember("setDelta", 1.25);
        var four = RenderBounds(env, data, portable);
        Assert.True(four.Height > three.Height, "A fourth reading must add height, not squeeze into the first row.");
        Assert.Equal(three.Width, four.Width, 5);
        var coolantY = Convert.ToDouble(env.CallMember("textY", "COOLANT")[0]);
        Assert.Equal(coolantY, Convert.ToDouble(env.CallMember("textY", "OIL PRESSURE")[0]), 5);
        Assert.Equal(coolantY, Convert.ToDouble(env.CallMember("textY", "ENGINE")[0]), 5);
        Assert.True(Convert.ToDouble(env.CallMember("textY", "DELTA")[0]) > coolantY);
        Assert.Contains("+1.25", (string)env.CallMember("render", data, portable)[0]);
        SetSetting(env, "showLocalBest", false);
        Assert.Equal(three.Height, RenderBounds(env, data, portable).Height, 5);
        Assert.DoesNotContain("DELTA", (string)env.CallMember("render", data, portable)[0]);
    }

    [Fact]
    public void DisablingGlassUsesRoundedFillForEverySurface()
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        RenderBounds(env, data);
        var surfaces = Convert.ToInt32(env["glassCalls"]);
        Assert.True(surfaces >= 2, "Exercise both the main panel and damage surface.");
        env.CallMember("rememberGlassBounds");
        SetSetting(env, "glassEnabled", false);
        RenderBounds(env, data);
        Assert.Equal(0, Convert.ToInt32(env["glassCalls"]));
        Assert.Equal(surfaces, Convert.ToInt32(env.CallMember("matchingFallbackSurfaces")[0]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.25)]
    [InlineData(.44)]
    [InlineData(1)]
    public void GlassOpacityAppliesToEveryGlassAndFallbackSurface(double opacity)
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        SetSetting(env, "glassOpacity", opacity);
        RenderBounds(env, data);
        var surfaces = Convert.ToInt32(env["glassCalls"]);
        Assert.True(surfaces >= 2);
        for (var i = 1; i <= surfaces; i++)
            Assert.InRange(Convert.ToDouble(env.CallMember("glassAlpha", i)[0]),
                Math.Max(0, opacity * 255 - 1), Math.Min(255, opacity * 255 + 1));
        env.CallMember("rememberGlassBounds");
        SetSetting(env, "glassEnabled", false);
        RenderBounds(env, data);
        Assert.Equal(0, Convert.ToInt32(env["glassCalls"]));
        Assert.Equal(surfaces, Convert.ToInt32(env.CallMember("matchingFallbackSurfaces")[0]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidGlassOpacityNeverReachesRendererOutOfRange(double opacity)
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        SetSetting(env, "glassOpacity", opacity);
        RenderBounds(env, data);
        var surfaces = Convert.ToInt32(env["glassCalls"]);
        Assert.True(surfaces >= 2);
        for (var i = 1; i <= surfaces; i++)
        {
            var alpha = Convert.ToDouble(env.CallMember("glassAlpha", i)[0]);
            Assert.True(double.IsFinite(alpha));
            Assert.InRange(alpha, 0, 255);
        }
        env.CallMember("rememberGlassBounds");
        SetSetting(env, "glassEnabled", false);
        RenderBounds(env, data);
        Assert.Equal(surfaces, Convert.ToInt32(env.CallMember("matchingFallbackSurfaces")[0]));
    }

    [Fact]
    public void OpacityChangesApplyToAnAlreadyRenderedDashboard()
    {
        using var lua = new Lua(); var env = Load(lua); var data = Sample();
        RenderBounds(env, data);
        var allocatedBrushes = Convert.ToInt32(env["brushCreates"]);
        foreach (var opacity in new[] { .2, .8, 0, 1, .44 })
        {
            SetSetting(env, "glassOpacity", opacity);
            RenderBounds(env, data);
            var surfaces = Convert.ToInt32(env["glassCalls"]);
            Assert.True(surfaces >= 2);
            for (var i = 1; i <= surfaces; i++)
                Assert.InRange(Convert.ToDouble(env.CallMember("glassAlpha", i)[0]),
                    Math.Max(0, opacity * 255 - 1), Math.Min(255, opacity * 255 + 1));
            Assert.Equal(allocatedBrushes, Convert.ToInt32(env["brushCreates"]));
        }
    }

    private static void SetSetting(LuaGlobal env, string name, object value) =>
        env.CallMember("setSetting", "dashboards.settings." + name, value);

    private static (double Width, double Height) RenderBounds(LuaGlobal env, GameData data, bool portable = true)
    {
        env.CallMember("render", data, portable);
        var bounds = env.CallMember("mainBounds");
        var width = Convert.ToDouble(bounds[0]); var height = Convert.ToDouble(bounds[1]);
        Assert.True(double.IsFinite(width) && width > 0);
        Assert.True(double.IsFinite(height) && height > 0);
        return (width, height);
    }

    private static GameData Sample() => new()
    {
        Speed=130, Gear=4, RPM=5500, MaxRPM=8000, MaxGears=6, Time=30, HandBrakeValid=true,
        Engine=new("test", default) { CoolantTemperatureCelsius=94 },
        Turbo=new("test", default) { PressureBar=1.24f, PressureReference=TurboPressureReference.Gauge },
        Hybrid=new("test", default) { BatteryChargeFraction=.8f, BoostProgressFraction=.5f, BoostActive=true },
        CarDamage=new("test", default, new() { IsAvailable=true, RadiatorAvailable=true, RadiatorSeverity=2 }, [], [], [])
    };

    private static LuaGlobal Load(Lua lua)
    {
        var env=lua.CreateEnvironment();
        env.DoChunk(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestData","commonfunc.lua")),"common");
        env.DoChunk(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestData","liquid-glass.lua")),"glass");
        env.DoChunk("""
            local function noop() end
            brushCreates=0
            local disposable={Dispose=noop}
            local settings={['dashboards.settings.size']=0.5,['dashboards.settings.positionH']=0,
                ['dashboards.settings.positionV']=0,['dashboards.settings.marginH']=0,['dashboards.settings.marginV']=0,
                ['dashboards.settings.glassEnabled']=true,['dashboards.settings.glassOpacity']=.44}
            local function bounds(brush,x1,y1,x2,y2)
                return {x=x1,y=y1,width=x2-x1,height=y2-y1,alpha=brush.Color.A}
            end
            local function color(r,g,b,a) return {R=r,G=g,B=b,A=a} end
            local gfx={Width=2000,Height=800,FPS=60,CreateFont=function() return disposable end,
                CreateSolidBrush=function(r,g,b,a)
                    brushCreates=brushCreates+1
                    return {Color=color(r,g,b,a),Dispose=noop}
                end,
                CreateGeometry=function() return {BeginFigure=noop,AddPoint=noop,AddCurveWithArcSegmentArgs=noop,
                    EndFigure=noop,Close=noop,Dispose=noop} end,
                DrawLine=function(brush)
                    local c=brush.Color
                    if c.R==255 and c.G==69 and c.B==86 then redIconLines=redIconLines+1 end
                end,FillGeometry=noop,FillRectangle=noop,DrawRoundedRectangle=noop,
                FillRoundedRectangle=function(brush,x1,y1,x2,y2)
                    table.insert(roundedFills,bounds(brush,x1,y1,x2,y2))
                end,FillCircle=noop,
                DrawGlassPanel=function(brush,x1,y1,x2,y2)
                    glassCalls=glassCalls+1;table.insert(glassPanels,bounds(brush,x1,y1,x2,y2))
                end,
                DrawCircle=noop,
                drawTextWithBackgroundCentered=function(font,size,brush,background,x,y,value)
                    table.insert(texts,value)
                    textPositions[value]={x=x,y=y}
                    local c=brush.Color
                    if value=='P' then handbrakeColor=c.R..','..c.G..','..c.B end
                end}
            local args={Graphics=gfx,Self={GetConfigByKey=function(key) return settings[key] end},
                GameOverlayDrawingHelper={getPoint=function(x,y) return {X=x,Y=y} end,getColor=color},
                I18NLoader={ResolveByKey=function(key) return key end},
                Config={HudShiftGreenZone=.5,HudShiftYellowZone=.7,HudShiftRedZone=.9},GameContext={}}
            onInit(args)
            function setSetting(key,value)
                settings[key]=value
            end
            function setDelta(value)
                args.GameContext={LocalReplayValid=true,GameState=GAMESTATE_Racing,
                    LocalReplayComparison={Valid=true,DeltaAtCurrentProgress=value}}
            end
            function textY(value) return textPositions[value].y end
            function mainBounds()
                local main=glassPanels[1]
                if main==nil then
                    for _,fill in ipairs(roundedFills) do
                        if main==nil or fill.width*fill.height>main.width*main.height then main=fill end
                    end
                end
                return main.width,main.height
            end
            function glassAlpha(index) return glassPanels[index].alpha end
            function rememberGlassBounds() rememberedGlass=glassPanels end
            function matchingFallbackSurfaces()
                local matches=0
                for _,panel in ipairs(rememberedGlass) do
                    for _,fill in ipairs(roundedFills) do
                        if math.abs(panel.x-fill.x)<.00001 and math.abs(panel.y-fill.y)<.00001
                            and math.abs(panel.width-fill.width)<.00001 and math.abs(panel.height-fill.height)<.00001
                            and math.abs(panel.alpha-fill.alpha)<=1 then
                            matches=matches+1;break
                        end
                    end
                end
                return matches
            end
            function render(data,portable)
                texts={};glassCalls=0;handbrakeColor='';glassPanels={};roundedFills={};redIconLines=0;textPositions={}
                args.GameData=data;args.IsWebDashboard=portable
                onUpdate(args)
                return table.concat(texts,'|')
            end
            ""","graphics");
        return env;
    }
}
