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

    _brushes["border"] = gfx.CreateSolidBrush(0x5d, 0x5b, 0x58, 100);
    _brushes["background"] = gfx.CreateSolidBrush(0x00, 0x00, 0x00, 100);
    _brushes["transparent"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x2c, 0x33, 0x3c, 255);
    _brushes["rpm"] = gfx.CreateSolidBrush(0x9c, 0x9e, 0x5c, 255)
    _brushes["brake"] = gfx.CreateSolidBrush(0xd2, 0x18, 0x1d, 255)
    _brushes["throttle"] = gfx.CreateSolidBrush(0xc3, 0xe1, 0x67, 255)
    _brushes["clutch"] = gfx.CreateSolidBrush(0x2a, 0xb4, 0x5d, 255)
    _brushes["hybrid"] = gfx.CreateSolidBrush(0x45, 0xf2, 0xee, 255)
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean Roman", 14);
    _fonts["wrcGear"] = gfx.CreateCustomFont("WRC Clean Roman", 32);
    _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function drawGeo(gfx, helper, centerX, centerY, startAngle, endAngle, radiusOuter, radiusInner, arcSize, brush)
    -- force cast to float???
    radiusOuter = radiusOuter + 0.0;
    radiusInner = radiusInner + 0.0;
    local geo_path = gfx.CreateGeometry();
    local pointOuterStart = helper.getPoint(centerX + radiusOuter * math.cos(startAngle),
        centerY - radiusOuter * math.sin(startAngle));
    local pointOuterEnd = helper.getPoint(centerX + radiusOuter * math.cos(endAngle),
        centerY - radiusOuter * math.sin(endAngle));
    local pointInnerStart = helper.getPoint(centerX + radiusInner * math.cos(startAngle),
        centerY - radiusInner * math.sin(startAngle));
    local pointInnerEnd = helper.getPoint(centerX + radiusInner * math.cos(endAngle),
        centerY - radiusInner * math.sin(endAngle));

    -- print("startAngle: " .. startAngle .. " endAngle: " .. endAngle .. " radiusOuter: " .. radiusOuter .. " radiusInner: " .. radiusInner);
    -- print("pointOuterStart: " .. pointOuterStart.X .. " " .. pointOuterStart.Y);
    -- print("PointOuterEnd: " .. pointOuterEnd.X .. " " .. pointOuterEnd.Y);
    -- print("pointInnerStart: " .. pointInnerStart.X .. " " .. pointInnerStart.Y);
    -- print("pointInnerEnd: " .. pointInnerEnd.X .. " " .. pointInnerEnd.Y);

    geo_path.BeginFigure(pointOuterStart, true);
    -- use full 5 parameters to make sure the correct method is called???
    -- fuck the stupid method
    geo_path.AddCurveWithArcSegmentArgs(pointOuterEnd, radiusOuter, arcSize, SWEEPDIRECTION_CLOCKWISE);
    geo_path.AddPoint(pointInnerEnd);
    geo_path.AddCurveWithArcSegmentArgs(pointInnerStart, radiusInner, arcSize, SWEEPDIRECTION_COUNTERCLOCKWISE);
    geo_path.EndFigure();
    geo_path.Close();

    gfx.FillGeometry(geo_path, brush);
end

function drawStaticFrames(gfx, self, data, helper, x, y, width, height) 
    -- print("drawing the static frames")
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    -- draw the static frames
    -- 1. background
    -- print("drawing the background")
    -- just draw the background (1278x352)
    gfx.DrawImage(self.ImageResources["images@background"], x, y, x + width, y + height);
end

function drawRPM(gfx, self, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local telemetryRadius = height * 0.378
    local rpmWeight = height * 0.01;
    local rpm = data.RPM;
    local maxRPM = data.MaxRPM
    if (rpm > 0) then
        local arcAngle = (6.9 * math.pi / 6) * rpm / maxRPM;
        local arcSize = ARCSIZE_SMALL;
        if (arcAngle > math.pi) then
            arcSize = ARCSIZE_LARGE;
        end
        -- RPM color should become from green to yellow and then red
        local rpmBrush = gfx.CreateSolidBrush(
            _brushes["rpm"].Color.R + (255 - _brushes["rpm"].Color.R) * rpm / maxRPM,
            _brushes["rpm"].Color.G - _brushes["rpm"].Color.G * rpm / maxRPM * 0.5,
            _brushes["rpm"].Color.B - _brushes["rpm"].Color.B * rpm / maxRPM,
            _brushes["rpm"].Color.A
        );
        drawGeo(gfx, helper, x + width * 0.471, y + height * 0.624, 6.45 * math.pi / 6, 6.45 * math.pi / 6 - arcAngle, telemetryRadius, telemetryRadius - rpmWeight, arcSize, rpmBrush);
        
        -- release the color
        rpmBrush.Dispose();
    end
end

function drawThrottle(gfx, self, data, helper, x, y, width, height)
    -- throttle is an image, on the right side, should use clip, 718x66
    local throttle = data.Throttle;
    local throttleWidthTotal = width * 0.39;
    local throttleClipWidth = throttleWidthTotal * (1-throttle);
    local throttleWHRatio = 718.0 / 66.0;
    local throttleHeight = throttleWidthTotal / throttleWHRatio;
    local widthEnd = x + width * 0.94;
    local heightStart = y + height * 0.88;
    gfx.ClipRegionStart(widthEnd - throttleWidthTotal, heightStart - throttleHeight, widthEnd - throttleClipWidth, heightStart);
    gfx.DrawImage(self.ImageResources["images@throttlebar"], widthEnd - throttleWidthTotal, heightStart - throttleHeight, widthEnd, heightStart);
    gfx.ClipRegionEnd();
end

function drawBrake(gfx, self, data, helper, x, y, width, height)
    -- throttle is an image, on the right side, should use clip, 634x58
    local brake = data.Brake;
    local brakeWidthTotal = width * 0.34;
    local brakeClipWidth = brakeWidthTotal * (1-brake);
    local brakeWHRatio = 634.0 / 58.0;
    local brakeHeight = brakeWidthTotal / brakeWHRatio;
    local widthStart = x + width * 0.053;
    local heightEnd = y + height * 0.85;
    gfx.ClipRegionStart(widthStart + brakeClipWidth, heightEnd - brakeHeight, widthStart + brakeWidthTotal, heightEnd);
    gfx.DrawImage(self.ImageResources["images@brakebar"], widthStart, heightEnd - brakeHeight, widthStart + brakeWidthTotal, heightEnd);
    gfx.ClipRegionEnd();
end

function drawClutch(gfx, self, data, helper, x, y, width, height)
    -- 71x145
    local clutch = data.Clutch
    local clutchHeightTotal = height * 0.31
    local clutchClipHeight = clutchHeightTotal * clutch;
    local clutchWHRatio = 71.0 / 145.0;
    local clutchWidth = clutchHeightTotal * clutchWHRatio;

    local widthStart = x + width * 0.368;
    local heightStart = y + height * 0.54;

    gfx.ClipRegionStart(widthStart, heightStart, widthStart + clutchWidth, heightStart + clutchClipHeight);
    gfx.DrawImage(self.ImageResources["images@clutch"], widthStart, heightStart, widthStart + clutchWidth, heightStart + clutchHeightTotal);
    gfx.ClipRegionEnd();
end

function drawHandBrake(gfx, self, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    local telemetryRadius = height * 0.351
    local heightStart = y + height * 0.62;
    local widthStart = x + width * 0.4705;

    local handBrake = data.HandBrake;
    local handBrakeWeight = height * 0.036;

    if (handBrake > 0) then
        local arcAngle = 53.0 * math.pi / 48.0 * handBrake;
        local arcSize = ARCSIZE_SMALL;
        if (arcAngle > math.pi) then
            arcSize = ARCSIZE_LARGE;
        end
        drawGeo(gfx, helper, widthStart, heightStart, 43 * math.pi / 48, -5.0 * math.pi / 24, telemetryRadius, telemetryRadius - handBrakeWeight, arcSize, _brushes["hybrid"]);
    end

    -- draw icon 40x33
    local iconWHRatio = 1.25;
    local iconStartX = x + width * 0.40;
    local iconStartY = y + height * 0.7;
    local iconWidth = height * 0.08;
    local iconHeight = iconWidth / iconWHRatio;
    if (handBrake > 0) then
        gfx.DrawImage(self.ImageResources["images@handbrake_active"], iconStartX, iconStartY, iconStartX + iconWidth, iconStartY + iconHeight);
    else
        gfx.DrawImage(self.ImageResources["images@handbrake"], iconStartX, iconStartY, iconStartX + iconWidth, iconStartY + iconHeight);
    end
end

function drawSpeed(gfx, self, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];

    local centerX = x + width * 0.468;
    local centerY = y + height * 0.55;

    local speedWeight = height * 0.25;
    local speed = data.Speed;

    gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["transparent"], centerX, centerY, math.floor(speed));
end

function drawGear(gfx, self, data, helper, x, y, width, height)
    -- right bottom
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];

    local gearWeight = height * 0.13;
    local gear = math.floor(data.Gear);
    local gearText = "";
    if (gear == -1 or gear == 10) then
        gearText = "R";
    end
    if (gear == 0) then
        gearText = "N";
    end
    if (gear > 0 and gear < 10) then
        gearText = tostring(gear);
    end
    gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["transparent"], x + width * 0.53, y + height * 0.73, gearText);
end


function onUpdate(args)
    local data = args.GameData;
    local gfx = args.Graphics;
    local conf = args.Config;
    local ctx = args.GameContext;
    local i18n = args.I18NLoader;
    local helper = args.GameOverlayDrawingHelper;
    local self = args.Self

    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    -- print("calulating the margin, padding, pos of each element")
    
    -- calculate the margin, padding, pos of each element
    ---- print("calculating the margin, padding, pos of each element"); 
    local size = gfx.Height * self.GetConfigByKey("dashboards.settings.size");
    local positionH = self.GetConfigByKey("dashboards.settings.positionH");
    local positionV = self.GetConfigByKey("dashboards.settings.positionV");
    local whRatio = 1278.0 / 352.0;

    -- print("calulating the margin, padding, pos of each element")
    
    -- calculate the margin, padding, pos of each element
    ---- print("calculating the margin, padding, pos of each element"); 
    local telemetryStartX = 0;
    if (positionH == -1) then
        -- -1 means left
        telemetryStartX = 0;
    else
        if (positionH == 1) then
            -- 1 means right
            telemetryStartX = gfx.Width - size * whRatio;
        else
            -- 0 means center
            telemetryStartX = gfx.Width / 2 - size * whRatio / 2;
        end
    end
    
    local telemetryStartY = 0;
    if (positionV == -1) then
        -- -1 means top
        telemetryStartY = 0;
    else
        if (positionV == 1) then
            -- 1 means bottom
            telemetryStartY = gfx.Height - size;
        else
            -- 0 means center
            telemetryStartY = gfx.Height / 2 - size / 2;
        end
    end
    local width = size * whRatio;

    drawStaticFrames(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, size);
    drawRPM(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, size);
    drawThrottle(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, size);
    drawBrake(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, size);
    drawClutch(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, size);
    drawSpeed(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, size);
    drawGear(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, size);
    drawHandBrake(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, size);
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
end
