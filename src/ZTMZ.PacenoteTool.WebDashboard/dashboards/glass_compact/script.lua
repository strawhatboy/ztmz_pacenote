local resources = {};

local function finite(value)
    local n = tonumber(value);
    if n == nil or n - n ~= 0 then return nil end
    return n;
end

local function clamp(value, low, high, fallback)
    return math.max(low, math.min(high, finite(value) or fallback));
end

-- RPM fill spans zero to maximum RPM. The shift warning is a separate
-- threshold, so an early shift point never compresses the tachometer range.
local function lights(data, config)
    local rpm = finite(data.RPM);
    if rpm == nil or rpm <= 0 then return 0, false end
    local maximum = finite(data.MaxRPM);
    if maximum == nil or maximum <= 0 then return 0, false end
    local first, last = finite(data.ShiftLightsRPMStart), finite(data.ShiftLightsRPMEnd);
    if not (data.ShiftLightsRPMValid and first ~= nil and last ~= nil and first >= 0 and last > first) then
        local red = finite(config.HudShiftRedZone);
        last = red ~= nil and red > 0 and maximum * red or nil;
    end
    if last ~= nil and rpm >= last then return 18, true end
    return math.min(18, math.ceil(rpm/maximum*18)), false;
end

function onInit(args)
    local gfx = args.Graphics;
    resources.brushes = {};
    for name, color in pairs({
        glass={16,18,21,82}, rim={235,247,255,110}, white={247,251,255,255},
        muted={205,210,215,255}, track={185,195,205,60}, line={240,245,250,190},
        red={255,69,70,255},
        shadow={2,8,15,190}, clear={0,0,0,0}
    }) do
        resources.brushes[name] = gfx.CreateSolidBrush(color[1],color[2],color[3],color[4]);
    end
    -- Fixed colours along the RPM scale, smoothly blended between four stops.
    resources.rpmBrushes = {};
    local stops={{0,247,251,255},{.4,100,225,140},{.7,255,215,70},{1,255,69,70}};
    for i=0,17 do
        local t=i/17;
        local index=1;
        while index<3 and t>stops[index+1][1] do index=index+1 end
        local a,b=stops[index],stops[index+1];
        local f=(t-a[1])/(b[1]-a[1]);
        local function mix(channel) return math.floor(a[channel]+(b[channel]-a[channel])*f+.5) end
        resources.rpmBrushes[i+1]=gfx.CreateSolidBrush(mix(2),mix(3),mix(4),255);
    end
    resources.numbers = gfx.CreateFont("Arial", 16, true, true);
    resources.units = gfx.CreateFont("Arial", 16, false, true);
end

function onUpdate(args)
    local gfx, data, helper = args.Graphics, args.GameData, args.GameOverlayDrawingHelper;
    local b = resources.brushes;
    local function setting(key) return args.Self.GetConfigByKey("dashboards.settings."..key) end
    local width, height = 340, 124;
    local s = math.min(gfx.Height*clamp(setting("size"),.06,.32,.12)/height,
        gfx.Width*.96/width,gfx.Height*.96/height);
    local w,h = width*s,height*s;
    local x,y = (gfx.Width-w)/2,(gfx.Height-h)/2;
    local mh = gfx.Width*clamp(setting("marginH"),0,.5,.02);
    local mv = gfx.Height*clamp(setting("marginV"),0,.5,.025);
    if setting("positionH")==-1 then x=mh elseif setting("positionH")==1 then x=gfx.Width-w-mh end
    if setting("positionV")==-1 then y=mv elseif setting("positionV")==1 then y=gfx.Height-h-mv end
    x=math.max(0,math.min(x,gfx.Width-w)); y=math.max(0,math.min(y,gfx.Height-h));
    local function tx(v) return x+v*s end
    local function ty(v) return y+v*s end
    local alpha=math.floor(clamp(setting("glassOpacity"),0,1,.32)*255+.5);
    if resources.alpha ~= alpha then
        b.glass.Color=helper.getColor(16,18,21,alpha);
        resources.alpha=alpha;
    end
    local slant,radius=28,10;
    -- Match the GPU's inverse-shear shape, including the standalone fallback.
    local outline=gfx.CreateGeometry();
    local innerWidth=width-slant;
    local centers={{innerWidth-radius,radius},{innerWidth-radius,height-radius},
        {radius,height-radius},{radius,radius}};
    for corner,center in ipairs(centers) do
        for step=0,8 do
            local angle=((corner-2)*.5+step/16)*math.pi;
            local py=center[2]+radius*math.sin(angle);
            local px=center[1]+radius*math.cos(angle)+slant*(1-py/height);
            local point=helper.getPoint(tx(px),ty(py));
            if corner==1 and step==0 then outline.BeginFigure(point,true) else outline.AddPoint(point) end
        end
    end
    outline.EndFigure();outline.Close();
    if setting("glassEnabled") ~= false and args.IsWebDashboard then
        gfx.DrawGlassPanel(b.glass,x,y,x+w,y+h,radius*s,
            clamp(setting("glassRefraction"),0,24,8)*s,clamp(setting("glassBlur"),0,8,4)*s,slant*s);
    else
        gfx.FillGeometry(outline,b.glass);
    end
    gfx.DrawGeometry(outline,b.rim,math.max(.7,s*.8));outline.Dispose();

    local count, atShift = lights(data,args.Config);
    local visible = not atShift or math.floor((finite(data.Time) or 0)*8)%2==0;
    for i=0,17 do
        local t=i/17;
        local px,py=104+196*t,41-22*(2*t-t*t);
        local segmentHeight=6+12*t;
        local brush=b.track;
        if i<count and visible then
            brush=atShift and b.red or resources.rpmBrushes[i+1];
        end
        gfx.DrawLine(brush,tx(px),ty(py+segmentHeight/2),tx(px+3),ty(py-segmentHeight/2),(4+7*t)*s);
    end
    local function text(value,px,py,size,font,brush)
        gfx.drawTextWithBackgroundCentered(font,size*s,b.shadow,b.clear,tx(px+.7),ty(py+.9),value);
        gfx.drawTextWithBackgroundCentered(font,size*s,brush,b.clear,tx(px),ty(py),value);
    end
    local gear=finite(data.Gear);
    local gearText=gear ~= nil and getGear(gear) or "";
    local speed=finite(data.Speed);
    text(gearText ~= "" and gearText or "—",69,79,70,resources.numbers,b.white);
    gfx.DrawLine(b.line,tx(124),ty(60),tx(115),ty(103),s*.8);
    text(speed ~= nil and string.format("%.0f",math.abs(speed)) or "—",199,80,46,resources.numbers,b.white);
    text("km/h",278,88,18,resources.units,b.muted);
end

function onExit()
    for _,brush in pairs(resources.brushes) do brush.Dispose() end
    for _,brush in ipairs(resources.rpmBrushes) do brush.Dispose() end
    resources.numbers.Dispose();resources.units.Dispose();
end
