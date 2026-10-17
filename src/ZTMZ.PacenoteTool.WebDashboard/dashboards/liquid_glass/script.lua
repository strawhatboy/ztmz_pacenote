local resources = {};

local function number(value)
    local n = tonumber(value);
    if n == nil or n - n ~= 0 then return nil end
    return n;
end

local function fraction(value)
    return math.max(0, math.min(1, number(value) or 0));
end

function onInit(args)
    local gfx = args.Graphics;
    local colors = {
        glass={14,23,34,112}, rim={235,247,255,72}, white={247,251,255,255},
        muted={151,169,185,255}, track={185,210,230,32}, green={164,236,172,255},
        amber={255,204,115,255}, red={255,69,86,255}, cyan={141,229,236,255},
        clear={0,0,0,0}, shadow={2,8,15,190}, blue={165,190,255,255}, line={216,234,248,35}
    };
    resources.brushes = {};
    for name, c in pairs(colors) do
        resources.brushes[name] = gfx.CreateSolidBrush(c[1],c[2],c[3],c[4]);
    end
    resources.labels = {};
    for key, fallback in pairs({gear="GEAR",coolant="COOLANT",oil="OIL",oilPressure="OIL PRESSURE",delta="DELTA",
        turbo="TURBO / bar",absolute="INTAKE / bar abs",pressure="PRESSURE / bar",engine="ENGINE",
        cooling="COOLING",gearbox="GEARBOX",boost="BOOST",ready="READY",regen="REGEN",standby="STANDBY",hybrid="HYBRID"}) do
        local id="dashboards.liquidGlass.labels."..key;
        local label=args.I18NLoader.ResolveByKey(id);
        resources.labels[key]=(label ~= nil and label ~= "" and label ~= id) and label or fallback;
    end
    resources.font = gfx.CreateFont("Segoe UI", 16);
    resources.bold = gfx.CreateFont("Segoe UI", 16, true);
end

local function bolt(gfx, helper, brush, x, y, s)
    local shape = gfx.CreateGeometry();
    shape.BeginFigure(helper.getPoint(x+s*.2,y-s*.65),true);
    for _,p in ipairs({{-.4,.08},{-.07,.08},{-.2,.65},{.4,-.1},{.07,-.1}}) do
        shape.AddPoint(helper.getPoint(x+s*p[1],y+s*p[2]));
    end
    shape.EndFigure(); shape.Close(); gfx.FillGeometry(shape,brush); shape.Dispose();
end

local function ring(gfx, helper, brush, x, y, r, value, width)
    local start, span = 4*math.pi/3, 5*math.pi/3;
    drawGeo(gfx,helper,x,y,start,start-span,r,r-width,ARCSIZE_LARGE,resources.brushes.track);
    if value ~= nil and value > 0 then
        local sweep = span*fraction(value);
        drawGeo(gfx,helper,x,y,start,start-sweep,r,r-width,
            sweep > math.pi and ARCSIZE_LARGE or ARCSIZE_SMALL,brush);
    end
end

-- Outline symbols remain readable at dashboard scale without external assets.
local function damageIcon(gfx, brush, kind, cx, cy, s)
    local function line(x1,y1,x2,y2)
        gfx.DrawLine(brush,cx+x1*s,cy+y1*s,cx+x2*s,cy+y2*s,1.7*s);
    end
    if kind == "engine" then
        for _,p in ipairs({{-15,-7,-10,-7},{-10,-7,-7,-12},{-7,-12,7,-12},{7,-12,10,-7},
            {10,-7,14,-7},{14,-7,14,10},{14,10,-10,10},{-10,10,-15,5},{-15,5,-15,-7},
            {-6,-16,6,-16},{0,-16,0,-12},{-19,-5,-19,5},{-19,0,-15,0},{14,-3,19,-6},{19,-6,19,8},{19,8,14,5}}) do
            line(p[1],p[2],p[3],p[4]);
        end
    elseif kind == "cooling" then
        gfx.DrawRoundedRectangle(brush,cx-15*s,cy-12*s,cx+15*s,cy+12*s,3*s,1.7*s);
        for i=-2,2 do line(i*5,-8,i*5,8) end
        line(-20,-5,-15,-5); line(15,6,20,6); line(-5,-16,5,-16);
    elseif kind == "oil" then
        for _,p in ipairs({{-15,-7,5,-7},{5,-7,15,-14},{15,-14,19,-10},{19,-10,7,1},
            {7,1,7,10},{7,10,-15,10},{-15,10,-15,-7},{-8,-11,0,-11},{-4,-11,-4,-7},
            {-15,-4,-21,-4},{-21,-4,-21,5},{-21,5,-15,5}}) do line(p[1],p[2],p[3],p[4]) end
        gfx.FillCircle(brush,cx+19*s,cy+4*s,2*s);
    elseif kind == "gearbox" then
        line(-12,-10,-12,10);line(0,-10,0,10);line(12,-10,12,0);line(-12,0,12,0);
        for _,p in ipairs({{-12,-13},{0,-13},{12,-13},{-12,13},{0,13}}) do
            gfx.DrawCircle(brush,cx+p[1]*s,cy+p[2]*s,3*s,1.5*s);
        end
    else
        gfx.DrawRoundedRectangle(brush,cx-10*s,cy-15*s,cx+10*s,cy+15*s,6*s,1.7*s);
        line(-4,-12,-4,12);line(4,-12,4,12);
        for i=-1,1 do line(-10,i*8,-5,i*8+3);line(5,i*8-3,10,i*8) end
    end
end

function onUpdate(args)
    local gfx, data, self, helper = args.Graphics,args.GameData,args.Self,args.GameOverlayDrawingHelper;
    local b = resources.brushes;
    local function setting(key) return self.GetConfigByKey("dashboards.settings."..key) end
    -- Resolve availability before layout. Unsupported telemetry occupies no slot.
    local hybrid = setting("showHybrid") ~= false and getHybridDisplayState(data) or nil;
    local turbo = setting("showTurbo") ~= false and data.Turbo or nil;
    local pressure = turbo ~= nil and number(turbo.PressureBar) or nil;
    local readings = {};
    local engine = data.Engine;
    if setting("showTemperatures") ~= false and engine ~= nil then
        local coolant, oil = number(engine.CoolantTemperatureCelsius), number(engine.OilTemperatureCelsius);
        if coolant ~= nil then
            table.insert(readings,{string.format("%.0f °C",coolant),resources.labels.coolant,
                engine.CoolantTemperatureWarning == true and b.red or b.white});
        elseif engine.CoolantTemperatureWarning == true then
            table.insert(readings,{"!",resources.labels.coolant,b.red,"cooling"});
        end
        if engine.OilPressureWarning == true then
            table.insert(readings,{"!",resources.labels.oilPressure,b.red,"oil"});
        elseif oil ~= nil then
            table.insert(readings,{string.format("%.0f °C",oil),resources.labels.oil,b.white});
        end
        if engine.EngineWarning == true then
            table.insert(readings,{"!",resources.labels.engine,b.red,"engine"});
        end
    end
    local ctx=args.GameContext;
    if setting("showLocalBest") ~= false and ctx ~= nil and ctx.LocalReplayValid == true
        and ctx.GameState == GAMESTATE_Racing then
        local comparison=ctx.LocalReplayComparison;
        local delta=comparison ~= nil and comparison.Valid == true and number(comparison.DeltaAtCurrentProgress) or nil;
        if delta ~= nil then table.insert(readings,{string.format("%+.2f",delta),resources.labels.delta,delta<=0 and b.green or b.red}) end
    end
    local damageItems={};
    if setting("showDamage") ~= false and data.CarDamage ~= nil then
        local damage=data.CarDamage.Dashboard;
        if damage ~= nil and damage.IsAvailable == true then
            -- Explicit access supports both CLR records and browser Lua tables.
            for _,item in ipairs({
                {damage.EngineAvailable,damage.EngineSeverity,"engine",resources.labels.engine},
                {damage.RadiatorAvailable,damage.RadiatorSeverity,"cooling",resources.labels.cooling},
                {damage.GearboxAvailable,damage.GearboxSeverity,"gearbox",resources.labels.gearbox},
                {damage.FrontLeftTyreAvailable,damage.FrontLeftTyreSeverity,"tyre","FL"},
                {damage.FrontRightTyreAvailable,damage.FrontRightTyreSeverity,"tyre","FR"},
                {damage.RearLeftTyreAvailable,damage.RearLeftTyreSeverity,"tyre","RL"},
                {damage.RearRightTyreAvailable,damage.RearRightTyreSeverity,"tyre","RR"}}) do
                local severity=number(item[2]);
                if item[1] == true and severity ~= nil and severity>=0 then
                    table.insert(damageItems,{item[3],item[4],severity});
                end
            end
        end
    end
    local width=370+(hybrid ~= nil and 96 or 0)+(pressure ~= nil and 110 or 0);
    local coreHeight, readingHeight = 146, 28;
    local panelHeight=coreHeight+math.ceil(#readings/3)*readingHeight;
    local height=panelHeight+(#damageItems>0 and 40 or 0);
    -- Keep instrument sizes stable as optional columns/rows appear or disappear.
    local s=gfx.Height*(number(setting("size")) or .32)/272;
    s=math.min(s,gfx.Width*.96/width,gfx.Height*.96/height);
    local w,h=width*s,height*s;
    local x,y=(gfx.Width-w)/2,(gfx.Height-h)/2;
    local mh=(number(setting("marginH")) or 0)*gfx.Width;
    local mv=(number(setting("marginV")) or 0)*gfx.Height;
    if setting("positionH")==-1 then x=mh elseif setting("positionH")==1 then x=gfx.Width-w-mh end
    if setting("positionV")==-1 then y=mv elseif setting("positionV")==1 then y=gfx.Height-h-mv end
    x=math.max(0,math.min(x,gfx.Width-w)); y=math.max(0,math.min(y,gfx.Height-h));
    local function tx(px) return x+px*s end
    local function ty(py) return y+py*s end
    local function text(value,px,py,size,brush,bold)
        local font=bold and resources.bold or resources.font;
        gfx.drawTextWithBackgroundCentered(font,size*s,b.shadow,b.clear,tx(px+.7),ty(py+.9),value);
        gfx.drawTextWithBackgroundCentered(font,size*s,brush or b.white,b.clear,tx(px),ty(py),value);
    end
    local function line(px,py,qx,qy,brush,thickness)
        gfx.DrawLine(brush,tx(px),ty(py),tx(qx),ty(qy),(thickness or 1)*s);
    end
    local opacity=math.floor(fraction(number(setting("glassOpacity")) or .44)*255+.5);
    if resources.glassOpacity ~= opacity then
        b.glass.Color=helper.getColor(14,23,34,opacity);
        resources.glassOpacity=opacity;
    end
    local function glass(px,py,pw,ph,r)
        -- An off switch emits ordinary geometry: no glass command, capture or shader.
        if setting("glassEnabled") ~= false and args.IsWebDashboard then
            gfx.DrawGlassPanel(b.glass,tx(px),ty(py),tx(px+pw),ty(py+ph),r*s,
                math.max(0,math.min(24,number(setting("glassRefraction")) or 8))*s,
                math.max(0,math.min(8,number(setting("glassBlur")) or 4))*s);
        else
            gfx.FillRoundedRectangle(b.glass,tx(px),ty(py),tx(px+pw),ty(py+ph),r*s);
        end
        gfx.DrawRoundedRectangle(b.rim,tx(px),ty(py),tx(px+pw),ty(py+ph),r*s,s);
    end
    glass(0,0,width,panelHeight,24);
    local level=getRPMLevel(data,args.Config);
    local blink=level ~= 4 or math.floor((number(data.Time) or 0)*8)%2==0;
    for i=0,17 do
        local brush=b.track;
        if level>0 and i<math.min(level,3)*6 and blink then
            brush=i<6 and b.green or (i<12 and b.amber or b.red);
        end
        gfx.FillRoundedRectangle(brush,tx(106+i*14),ty(12),tx(116+i*14),ty(18),2*s);
    end
    text(string.format("%.0f RPM",number(data.RPM) or 0),225,29,10,b.muted);
    text(getGear(number(data.Gear) or 0),155,75,80,b.white,true);
    text(resources.labels.gear,155,118,10,b.muted);
    text(string.format("%.0f",number(data.Speed) or 0),275,76,57,b.white,true);
    text("km/h",275,112,12,b.muted);
    line(207,45,207,113,b.line);
    for i,p in ipairs({{"T",data.Throttle,b.green},{"B",data.Brake,b.red},{"C",data.Clutch,b.blue}}) do
        local px=26+(i-1)*20;
        gfx.FillRoundedRectangle(b.track,tx(px),ty(38),tx(px+8),ty(100),4*s);
        local amount=fraction(p[2]);
        if amount>0 then gfx.FillRoundedRectangle(p[3],tx(px),ty(100-62*amount),tx(px+8),ty(100),4*s) end
        text(p[1],px+4,112,9,b.muted,true);
    end
    line(115,132,320,132,b.line,2);
    line(217.5,128,217.5,136,b.muted);
    gfx.FillCircle(b.white,tx(217.5+102.5*math.max(-1,math.min(1,number(data.Steering) or 0))),ty(132),2.5*s);
    if data.HandBrakeValid == true then
        local brush=(number(data.HandBrake) or 0)>0 and b.red or b.muted;
        gfx.DrawCircle(brush,tx(50),ty(131),9*s,1.4*s);
        text("P",50,131,13,brush,true);
        drawGeo(gfx,helper,tx(50),ty(131),5*math.pi/4,3*math.pi/4,13*s,11.5*s,ARCSIZE_SMALL,brush);
        drawGeo(gfx,helper,tx(50),ty(131),math.pi/4,-math.pi/4,13*s,11.5*s,ARCSIZE_SMALL,brush);
    end
    local nextColumn=370;
    if hybrid ~= nil then
        local px=nextColumn+42;
        line(nextColumn-8,36,nextColumn-8,137,b.line);
        local active=({boost=b.amber,ready=b.green,regen=b.cyan,standby=b.muted,hybrid=b.muted})[hybrid.state];
        ring(gfx,helper,b.cyan,tx(px),ty(72),29*s,hybrid.battery,2.5*s);
        ring(gfx,helper,active,tx(px),ty(72),22*s,hybrid.progress,2.5*s);
        bolt(gfx,helper,active,tx(px),ty(72),17*s);
        if hybrid.battery ~= nil then text(string.format("%.0f%%",hybrid.battery*100),px,112,18,b.cyan,true) end
        local label=resources.labels[hybrid.state];
        if hybrid.map ~= nil then label=label.."  M"..string.format("%.0f",hybrid.map) end
        text(label,px,132,9,active);
        nextColumn=nextColumn+96;
    end
    if pressure ~= nil then
        local px=nextColumn+48;
        line(nextColumn-8,36,nextColumn-8,137,b.line);
        local label=resources.labels.pressure;
        if turbo.PressureReference=="gauge" then label=resources.labels.turbo
        elseif turbo.PressureReference=="absolute" then label=resources.labels.absolute end
        gfx.DrawCircle(b.track,tx(px),ty(72),32*s,1.5*s);
        text(string.format("%.2f",pressure),px,72,24,b.white,true);
        text(label,px,124,9,b.muted);
    end
    if #readings>0 then
        line(18,coreHeight,width-18,coreHeight,b.line);
        for i,item in ipairs(readings) do
            local row=math.floor((i-1)/3);
            local count=math.min(3,#readings-row*3);
            local px=((i-1)%3+.5)*width/count;
            local py=coreHeight+readingHeight/2+row*readingHeight;
            if item[4] ~= nil then
                damageIcon(gfx,item[3],item[4],tx(px+26),ty(py),.45*s);
            else
                -- Bound long race deltas to their cell without clipping the panel.
                local valueSize=item[2]==resources.labels.delta and math.min(16,64/(math.max(1,#item[1])*.62)) or 16;
                text(item[1],px+26,py,valueSize,item[3],true);
            end
            text(item[2],px-34,py,8,item[4] ~= nil and b.red or b.muted);
        end
    end
    if #damageItems>0 then
        local footerWidth=math.min(width,#damageItems*70+16);
        local footerY=panelHeight+4;
        glass((width-footerWidth)/2,footerY,footerWidth,36,16);
        for i,item in ipairs(damageItems) do
            local px=(width-footerWidth)/2+(i-.5)*footerWidth/#damageItems;
            local brush=item[3]>=3 and b.red or (item[3]>0 and b.amber or b.muted);
            damageIcon(gfx,brush,item[1],tx(px),ty(footerY+12),.48*s);
            text(item[2],px,footerY+27,8,brush,true);
        end
    end
end

function onExit()
    for _,brush in pairs(resources.brushes) do brush.Dispose() end
    resources.font.Dispose();resources.bold.Dispose();
end
