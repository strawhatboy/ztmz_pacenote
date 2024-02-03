local resources = {};
local MAX_SPEED = 200;
local MAX_WHEEL_SPEED = 220;
local MAX_WHEEL_TEMP = 800;
local MAX_G_FORCE = 2.2;
local MAX_SUSPENSION_SPD = 1000; -- m/s
local MIN_SUSPENSION_SPD = -1000;
local MAX_SUSPENSION = 200;
local MIN_SUSPENSION = -200;

local SWEEPDIRECTION_COUNTERCLOCKWISE = 0;
local SWEEPDIRECTION_CLOCKWISE = 1;

local ARCSIZE_SMALL = 0;
local ARCSIZE_LARGE = 1;

-- This function is called when the script is loaded
-- args: dotnet object
--     args.Graphics: GameOverlay.Drawing.Graphics
--     args.Config: ZTMZ.PacenoteTool.Base.Config
--     args.I18NLoader: ZTMZ.PacenoteTool.Base.I18NLoader
--     args.GameData: ZTMZ.PacenoteTool.Base.Game.GameData
--     args.GameOverlayDrawingHelper: ZTMZ.PacenoteTool.GameOverlayDrawingHelper
--     args.GameContext: 
--         args.GameContext.TrackName: string
--         args.GameContext.AudioPackage: string
--         args.GameContext.ScriptAuthor: string
--         args.GameContext.PacenoteType: string

function onInit(args)
    local gfx = args.Graphics;
    local conf = args.Config;
    local _brushes = {};
    _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
    _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0);
    _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
    if (conf.HudChromaKeyMode) then
        _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
        _brushes["blue"] = gfx.CreateSolidBrush(255, 0, 255);
        _brushes["clear"] = gfx.CreateSolidBrush(0, 255, 0);
    else
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
        _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
        _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    end

    _brushes["grid"] = gfx.CreateSolidBrush(255, 255, 255, 0.2);
    _brushes["background"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 100);
    _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x3, 0x6, 0xF, 255);
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function drawGBall(gfx, conf, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local centerX = x + 0.5 * width;
    local centerY = y + 0.5 * height;
    local radius = math.min(width, height) * 0.5;
    gfx.FillCircle(_brushes["grey"], centerX, centerY, radius);
    gfx.DrawLine(_brushes["grid"], centerX - radius, centerY, centerX + radius, centerY, 1);
    gfx.DrawLine(_brushes["grid"], centerX, centerY - radius, centerX, centerY + radius, 1);
    gfx.DrawCircle(_brushes["white"], centerX, centerY, radius -1 , 1);
    gfx.DrawCircle(_brushes["black"], centerX, centerY, radius , 1);
    -- the ball
    local ballX = centerX + data.G_lat * radius / MAX_G_FORCE;
    local ballY = centerY + data.G_long * radius / MAX_G_FORCE;
    gfx.FillCircle(_brushes["red"], ballX, ballY, radius * 0.1);
end

function drawSpdSector(gfx, conf, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local maxSpeed = math.max(maxSpeed, data.Speed); 
    helper.drawSector(gfx, "SPD (KM/h)", x, y, width, height, data.Speed, maxSpeed, _brushes["black"], _brushes["white"], _brushes["grey"], _fonts["consolas"], conf.HudSectorThicknessRatio);
end

function drawPedals(gfx, conf, data, helper, x, y, width, height)
    -- print("drawPedals on " .. x .. ", " .. y .. ", " .. width .. ", " .. height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    -- 3 pedals
    local pedalWidth = 1.0 / 3.6 * width;
    local spacing = 0.3 / 3.6 * width;
    gfx.FillRectangle(_brushes["grey"], x, y, x + pedalWidth, y + height);
    gfx.FillRectangle(_brushes["grey"], x + pedalWidth + spacing, y, x + 2 * pedalWidth + spacing, y + height);
    gfx.FillRectangle(_brushes["grey"], x + 2 * pedalWidth + 2 * spacing, y, x + width, y + height);
    gfx.DrawRectangle(_brushes["white"], x, y, x + pedalWidth, y + height, 1);
    gfx.DrawRectangle(_brushes["white"], x + pedalWidth + spacing, y, x + 2 * pedalWidth + spacing, y + height, 1);
    gfx.DrawRectangle(_brushes["white"], x + 2 * pedalWidth + 2 * spacing, y, x + width, y + height, 1);
    gfx.DrawRectangle(_brushes["black"], x-1, y-1, x + pedalWidth+1, y + height+1, 1);
    gfx.DrawRectangle(_brushes["black"], x + pedalWidth + spacing-1, y-1, x + 2 * pedalWidth + spacing+1, y + height+1, 1);
    gfx.DrawRectangle(_brushes["black"], x + 2 * pedalWidth + 2 * spacing-1, y-1, x + width+1, y + height+1, 1);

    gfx.FillRectangle(_brushes["blue"], 1 + x, 1 + y + height * (1-data.Clutch), x + pedalWidth - 1, y + height - 1);
    gfx.FillRectangle(_brushes["red"], 1 + x + pedalWidth + spacing, 1 + y + height * (1-data.Brake), x + 2 * pedalWidth + spacing - 1, y + height - 1);
    gfx.FillRectangle(_brushes["green"], 1 + x + 2 * pedalWidth + 2 * spacing, 1 + y + height * (1-data.Throttle), x + width - 1, y + height - 1);
end

function drawGear(gfx, conf, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    -- var font = gfx.CreateFont("consolas", width);
    -- var actualSize = MathF.Min(width, height);
    -- gfx.DrawText(_fonts["consolas"], actualSize, _brushes["white"], x, y, getGearText(Convert.ToInt32(UdpMessage.Gear)));
    local columns = math.floor(math.ceil(data.MaxGears * 0.5)) + 1;

    local barWidth = width / (columns + (columns-1) * conf.HudSectorThicknessRatio);
    local spacingH = conf.HudSectorThicknessRatio * barWidth;
    local barHeight = height / (2 + conf.HudSectorThicknessRatio);
    local spacingV = barHeight * conf.HudSectorThicknessRatio;

    local rectangles = {
        -- R
        helper.getRectangle(x, y, x + barWidth, y + barHeight),
    };

    for i = 1,data.MaxGears do
        local row = (i + 1) % 2;
        local column = (i + 1) / 2;
        table.insert(rectangles, helper.getRectangle(
            x + column * (spacingH + barWidth),
            y + row * (spacingV + barHeight),
            x + barWidth + column * (spacingH + barWidth),
            y + barHeight + row * (spacingV + barHeight)
        ));
    end

    for k,v in ipairs(rectangles) do
        gfx.FillRectangle(_brushes["white"], v);
        gfx.DrawRectangle(_brushes["black"], v, 1);
    end

    local gear = math.floor(data.Gear);
    local gearText = "";
    local isNGear = false;
    local rect;
    if (gear == -1 or gear == 10) then
        rect = rectangles[0];
        gearText = "R";
    end
    if (gear == 0) then
        rect = rectangles[0];
        isNGear = true;
        gearText = "N";
    end
    if (gear > 0 and gear < 10) then
        rect = rectangles[gear];
        gearText = tostring(gear);
    end

    if (not isNGear) then
        gfx.FillRectangle(_brushes["red"], rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Bottom - 1);
    end
    gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
        barWidth * 1.5,
        _brushes["white"],
        _brushes["black"],
        x + 0.5 * barWidth,
        y + spacingV + 1.5 * barHeight,
        gearText);
end

function drawSteering(gfx, conf, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local centerX = x + 0.5 * width;
    local centerY = y + 0.5 * height;
    local radiusOuter = math.min(width, height) * 0.5;
    local radiusInner = radiusOuter * (1 - conf.HudSectorThicknessRatio);
    local radiusWidth = radiusOuter - radiusInner;

    local rawSteeringAngle = data.Steering * conf.HudTelemetrySteeringDegree * 0.5;
    -- bg
    local pathBrush;
    local bgBrush;
    if (math.abs(rawSteeringAngle) >= 360) then
        pathBrush = _brushes["blue"];
        bgBrush = _brushes["white"];
    else
        pathBrush = _brushes["white"];
        bgBrush = _brushes["grey"];
    end
    gfx.FillCircle(bgBrush, centerX, centerY, radiusOuter);
    
    
    local steeringAngle = 90 - rawSteeringAngle;
    steeringAngle = steeringAngle / 180.0 * math.pi; -- to radian
    local middle = 0.5 * (radiusInner + radiusOuter);

    
    
    gfx.FillCircle(_brushes["black"], centerX, centerY, radiusInner);
    gfx.DrawCircle(_brushes["black"], centerX, centerY, radiusOuter, 1);

    local anchorLeft = helper.getPoint(centerX + middle * math.cos(steeringAngle + math.pi * 0.5),
        centerY - middle * math.sin(steeringAngle + math.pi * 0.5));
    local anchorRight = helper.getPoint(centerX + middle * math.cos(steeringAngle - math.pi * 0.5),
        centerY - middle * math.sin(steeringAngle - math.pi * 0.5));
    local anchorBottom = helper.getPoint(centerX + middle * math.cos(steeringAngle + math.pi),
        centerY - middle * math.sin(steeringAngle + math.pi));
    
    -- cross
    gfx.DrawLine(bgBrush, anchorLeft, anchorRight, radiusWidth);
    gfx.DrawLine(bgBrush, centerX, centerY, anchorBottom.X, anchorBottom.Y, radiusWidth);

    -- cursor
    local alpha = math.pi / 30.0;
    radiusOuter = radiusOuter - 1;
    
    -- path
    local arcSize;
    if (math.fmod(math.abs(rawSteeringAngle), 360) >= 180) then
        arcSize = ARCSIZE_LARGE;
    else 
        arcSize = ARCSIZE_SMALL;
    end
    local sweepDirection = rawSteeringAngle > 0 and SWEEPDIRECTION_CLOCKWISE or SWEEPDIRECTION_COUNTERCLOCKWISE;
    local backsDirection = rawSteeringAngle < 0 and SWEEPDIRECTION_CLOCKWISE or SWEEPDIRECTION_COUNTERCLOCKWISE;
    local geo_path = gfx.CreateGeometry();
    geo_path.BeginFigure(helper.getPoint(centerX,
        centerY - radiusOuter), true);
    geo_path.AddCurve(helper.getPoint(centerX + radiusOuter * math.cos(steeringAngle),
        centerY - radiusOuter * math.sin(steeringAngle)), radiusOuter, arcSize, sweepDirection);
    geo_path.AddPoint(helper.getPoint(centerX + radiusInner * math.cos(steeringAngle),
        centerY - radiusInner * math.sin(steeringAngle)));
    geo_path.AddCurve(helper.getPoint(centerX,
        centerY - radiusInner), radiusInner, arcSize, backsDirection);
    geo_path.EndFigure();
    geo_path.Close();

    gfx.FillGeometry(geo_path, pathBrush);
    
    local geo_cur = gfx.CreateGeometry();
    geo_cur.BeginFigure(helper.getPoint(centerX + radiusOuter * math.cos(steeringAngle + alpha),
        centerY - radiusOuter * math.sin(steeringAngle + alpha)), true);
    geo_cur.AddCurve(helper.getPoint(centerX + radiusOuter * math.cos(steeringAngle - alpha),
        centerY - radiusOuter * math.sin(steeringAngle - alpha)), radiusOuter, ARCSIZE_SMALL, SWEEPDIRECTION_CLOCKWISE);
    geo_cur.AddPoint(helper.getPoint(centerX + radiusInner * math.cos(steeringAngle - alpha),
        centerY - radiusInner * math.sin(steeringAngle - alpha)));
    geo_cur.AddCurve(helper.getPoint(centerX + radiusInner * math.cos(steeringAngle + alpha),
        centerY - radiusInner * math.sin(steeringAngle + alpha)), radiusInner, ARCSIZE_SMALL, SWEEPDIRECTION_COUNTERCLOCKWISE);
    geo_cur.EndFigure();
    geo_cur.Close();
    
    gfx.FillGeometry(geo_cur, _brushes["red"]);
end

function drawRPMSector(gfx, conf, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    helper.drawSector(gfx, "RPM", x, y, width, height, data.RPM, data.MaxRPM, _brushes["black"], _brushes["white"], _brushes["grey"], _fonts["consolas"], conf.HudSectorThicknessRatio);
end


function onUpdate(args)
    local data = args.GameData;
    local gfx = args.Graphics;
    local conf = args.Config;
    local ctx = args.GameContext;
    local i18n = args.I18NLoader;
    local helper = args.GameOverlayDrawingHelper;

    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    local drawFuncs = {};
    if (conf.HudTelemetryShowGBall) then table.insert(drawFuncs, drawGBall); end
    if (conf.HudTelemetryShowSpdSector) then table.insert(drawFuncs, drawSpdSector); end
    if (conf.HudTelemetryShowRPMSector) then table.insert(drawFuncs, drawRPMSector); end
    if (conf.HudTelemetryShowPedals) then table.insert(drawFuncs, drawPedals); end
    if (conf.HudTelemetryShowGear) then table.insert(drawFuncs, drawGear); end
    if (conf.HudTelemetryShowSteering) then table.insert(drawFuncs, drawSteering); end
    
    -- calculate the margin, padding, pos of each element
    --print("calculating the margin, padding, pos of each element");
    local telemetryHeight = gfx.Height * conf.HudSizePercentage;
    local telemetryWidth = telemetryHeight * #drawFuncs; -- elements are squre?
    local telemetryStartPosX = 0.5 * (gfx.Width - telemetryWidth);
    local telemetryStartPosY = gfx.Height - telemetryHeight;

    local telemetryPaddingH = telemetryHeight * conf.HudPaddingH;
    local telemetryPaddingV = telemetryHeight * conf.HudPaddingV;

    local telemetrySpacing = telemetryHeight * conf.HudElementSpacing;
    
    -- drawBackground
    --print("drawBackground");
    _brushes["telemetryBackground"].Color = helper.getColor(
        _brushes["telemetryBackground"].Color.R,
        _brushes["telemetryBackground"].Color.G,
        _brushes["telemetryBackground"].Color.B,
        255 * conf.HudBackgroundOpacity);
    gfx.FillRectangle(_brushes["telemetryBackground"], 
        telemetryStartPosX,
        telemetryStartPosY,
        telemetryStartPosX + telemetryWidth,
        telemetryStartPosY + telemetryHeight);

    local elementStartX = telemetryStartPosX + telemetryPaddingH;
    local elementStartY = telemetryStartPosY + telemetryPaddingV;
    local elementHeight = telemetryHeight - telemetryPaddingV * 2.0;
    local elementWidth = ((telemetryWidth - telemetryPaddingH * 2.0) - (#drawFuncs-1) * telemetrySpacing) /
                    #drawFuncs;

    -- print("drawFuncs start, in total there are " .. #drawFuncs .. " functions");
    for k,t in ipairs(drawFuncs) do
        -- print("drawFuncs...")
        --try catch
        t(gfx, conf, data, helper, elementStartX, elementStartY, elementWidth, elementHeight);

        elementStartX = elementStartX + elementWidth + telemetrySpacing;
    end
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
end
