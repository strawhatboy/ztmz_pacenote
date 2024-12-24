local resources = {};

local framesCount = 0;
local bInBlink = true;

-- This function is called when the script is loaded
-- args: dotnet object
--     args.Graphics: GameOverlay.Drawing.Graphics
--     args.Config: ZTMZ.PacenoteTool.Base.Config
--     args.I18NLoader: ZTMZ.PacenoteTool.Base.I18NLoader
--     args.GameData: ZTMZ.PacenoteTool.Base.Game.GameData
--     args.GameOverlayDrawingHelper: ZTMZ.PacenoteTool.GameOverlayDrawingHelper
--     args.Self: ZTMZ.PacenoteTool.Base.UI.Dashboard
--     args.GameContext: 
--         args.GameContext.TrackName: string
--         args.GameContext.AudioPackage: string
--         args.GameContext.ScriptAuthor: string
--         args.GameContext.PacenoteType: string

function onInit(args)
    local gfx = args.Graphics;
    local conf = args.Config;
    local helper = args.GameOverlayDrawingHelper;
    local _brushes = {};
    _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
    _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0, 200);
    _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
    _brushes["rpm_low"] = gfx.CreateSolidBrush(0x60, 0x9d, 0x51);
    _brushes["rpm_medium"] = gfx.CreateSolidBrush(0xd3, 0xa9, 0x5b);
    _brushes["rpm_high"] = gfx.CreateSolidBrush(0xb9, 0x57, 0x4a);
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
    _brushes["telemetryBackground"] = gfx.CreateRadialGradientBrush(
        helper.getColor(0x2c, 0x33, 0x3c, 150),
        helper.getColor(0x2c, 0x33, 0x3c, 110),
        helper.getColor(0x2c, 0x33, 0x3c, 80),
        helper.getColor(0x2c, 0x33, 0x3c, 0));
    _brushes["rpm"] = gfx.CreateLinearGradientBrush(
        -- green
        helper.getColor(0x31, 0xd2, 0x1b, 255),
        -- yellow
        helper.getColor(0xd3, 0xec, 0x46, 255),
        -- red
        helper.getColor(0xfb, 0x21, 0x0d, 255)
    );
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

function drawStaticFrames(gfx, self, data, helper, x, y, width, height) 
    -- print("drawing the static frames")
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    -- draw the static frames
    -- 1. background
    -- print("drawing the background")
    
    local centerX = x + width * 0.471;
    local centerY = y + height * 0.62;
    local radius = height * 0.5
    _brushes["telemetryBackground"].SetCenter(centerX, centerY);
    _brushes["telemetryBackground"].SetRadius(radius, radius);
    -- 2. just draw the background (1278x352)
    gfx.FillCircle(_brushes["telemetryBackground"], centerX, centerY, radius);
    gfx.DrawImage(self.ImageResources["images@background"], x, y, x + width, y + height);
end

function drawRPM(gfx, self, data, conf, helper, x, y, width, height)
    framesCount = framesCount + 1;
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local telemetryRadius = height * 0.385
    local rpmWeight = height * 0.015;
    local rpm = data.RPM;
    local maxRPM = data.MaxRPM
    if (rpm > 0) then
        local arcAngle = (6.9 * math.pi / 6) * rpm / maxRPM;
        local arcSize = ARCSIZE_SMALL;
        if (arcAngle > math.pi) then
            arcSize = ARCSIZE_LARGE;
        end
        -- RPM color should become from green to yellow and then red
        _brushes["rpm"].SetRange(x + width * 0.471 - telemetryRadius, y + height * 0.62, x + width * 0.471 + telemetryRadius, y + height * 0.62);

        -- blink
        local rpmLevel = getRPMLevel(data, conf);
        if (rpmLevel == 4) then
            local framesLimit = BLINK_INTERVAL_FRAMES_PERCENTAGE * gfx.FPS;
            if (framesCount > framesLimit) then
                framesCount = 0;
                if (bInBlink) then
                    bInBlink = false;
                else
                    bInBlink = true;
                end
            end
            if (bInBlink) then
                drawGeo(gfx, helper, x + width * 0.471, y + height * 0.62, 6.45 * math.pi / 6, 6.45 * math.pi / 6 - arcAngle, telemetryRadius, telemetryRadius - rpmWeight, arcSize, _brushes["rpm"]);
            end
        else
            bInBlink = true;
            drawGeo(gfx, helper, x + width * 0.471, y + height * 0.62, 6.45 * math.pi / 6, 6.45 * math.pi / 6 - arcAngle, telemetryRadius, telemetryRadius - rpmWeight, arcSize, _brushes["rpm"]);
        end
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
    local clutchClipHeight = clutchHeightTotal * (1-clutch);
    local clutchWHRatio = 71.0 / 145.0;
    local clutchWidth = clutchHeightTotal * clutchWHRatio;

    local widthStart = x + width * 0.368;
    local heightStart = y + height * 0.54;

    gfx.ClipRegionStart(widthStart, heightStart + clutchClipHeight, widthStart + clutchWidth, heightStart + clutchHeightTotal);
    gfx.DrawImage(self.ImageResources["images@clutch"], widthStart, heightStart, widthStart + clutchWidth, heightStart + clutchHeightTotal);
    gfx.ClipRegionEnd();
end

function drawHandBrake(gfx, self, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    local telemetryRadius = height * 0.348
    local heightStart = y + height * 0.617;
    local widthStart = x + width * 0.470;

    local handBrake = data.HandBrake;
    local handBrakeWeight = height * 0.028;

    if (handBrake > 0) then
        local totalArcAngle = 53.0 * math.pi / 48.0;
        local arcAngle = totalArcAngle * (1-handBrake);
        local arcSize = ARCSIZE_SMALL;
        if (totalArcAngle - arcAngle > math.pi) then
            arcSize = ARCSIZE_LARGE;
        end
        drawGeo(gfx, helper, widthStart, heightStart, 43 * math.pi / 48 - arcAngle, -5.0 * math.pi / 24, telemetryRadius, telemetryRadius - handBrakeWeight, arcSize, _brushes["hybrid"]);
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

function drawSpeed(gfx, self, data, conf, helper, x, y, width, height, switchGearNSpeed)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];

    local centerX = x + width * 0.468;
    local centerY = y + height * 0.55;

    local speedWeight = height * 0.25;
    local speed = math.floor(data.Speed);
    local speedText = "";
    local maxGear = math.floor(data.MaxGears);
    if (switchGearNSpeed) then
        speed = math.floor(data.Gear);
        speedText = getGear(speed);
    else
        speedText = tostring(speed);
    end

    local rpmLevel = getRPMLevel(data, conf);

    --ugly code
    if (not bInBlink and switchGearNSpeed and speed < maxGear) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["red"], centerX, centerY, speedText);
    else
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["transparent"], centerX, centerY, speedText);
    end

    if (rpmLevel == 1 and switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["rpm_low"], centerX, centerY, speedText);
    end

    if (rpmLevel == 2 and switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["rpm_medium"], centerX, centerY, speedText);
    end

    if (rpmLevel == 3 and switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["rpm_high"], centerX, centerY, speedText);
    end
end

function drawGear(gfx, self, data, conf, helper, x, y, width, height, switchGearNSpeed)
    -- right bottom
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];

    local gearWeight = height * 0.13;
    local gear = math.floor(data.Gear);
    local gearText = getGear(gear);
    local maxGear = math.floor(data.MaxGears);
    if (switchGearNSpeed) then
        gear = math.floor(data.Speed);
        gearText = tostring(gear);
    end

    local rpmLevel = getRPMLevel(data, conf);

    --ugly code
    if (not bInBlink and not switchGearNSpeed and gear < maxGear) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["red"], x + width * 0.53, y + height * 0.73, gearText);
    else
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["transparent"], x + width * 0.53, y + height * 0.73, gearText);
    end
    
    if (rpmLevel == 1 and not switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["rpm_low"], x + width * 0.53, y + height * 0.73, gearText);
    end

    if (rpmLevel == 2 and not switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["rpm_medium"], x + width * 0.53, y + height * 0.73, gearText);
    end

    if (rpmLevel == 3 and not switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["rpm_high"], x + width * 0.53, y + height * 0.73, gearText);
    end
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

    local whRatio = 1278.0 / 352.0;
    local switchGearNSpeed = self.GetConfigByKey("dashboards.settings.switchGearNSpeed")
    local telemetryStartX, telemetryStartY, width, height = getDashboardPositionStart(self, gfx, whRatio);

    local useOffset = self.GetConfigByKey("dashboards.settings.useOffset");
    if (useOffset) then
        telemetryStartX = telemetryStartX + 0.029 * width;  -- center the dashboard?
    end

    drawStaticFrames(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);
    drawRPM(gfx, self, data, conf, helper, telemetryStartX, telemetryStartY, width, height);
    drawThrottle(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);
    drawBrake(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);
    drawClutch(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);
    drawHandBrake(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);

    drawSpeed(gfx, self, data, conf, helper, telemetryStartX, telemetryStartY, width, height, switchGearNSpeed);
    drawGear(gfx, self, data, conf, helper, telemetryStartX, telemetryStartY, width, height, switchGearNSpeed);
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end
