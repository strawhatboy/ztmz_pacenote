-- fixed 3:1 whRatio
-- 1/4 bottom of the dashboard for clutch, brake, throttle.
-- 1/8 top of the dashboard for rpm, 1/6 length for RPM text filled with green, and 5/6 length for real RPM filled from green to red with black background and black text on top
-- in the middle, 2/3 length for speed, black text and white background
-- 1/6 length for gear, white text with black background, when time to shift, background blink with red
-- 1/6 length for handbrake, grey icon with white background, when handbrake is on, icon becomes red

local resources = {};
local framesCount = 0;
local bInBlink = true;
local whRatio = 3.0;
local fontFactor = 0.8;

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
    local _brushes = {};
    local helper = args.GameOverlayDrawingHelper;
    _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
    _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0, 200);
    _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
    _brushes["green"] = gfx.CreateSolidBrush(0x31, 0xd2, 0x1b); -- bright green
    _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
    _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    _brushes["rpm_low"] = gfx.CreateSolidBrush(0x60, 0x9d, 0x51);
    _brushes["rpm_medium"] = gfx.CreateSolidBrush(0xd3, 0xa9, 0x5b);
    _brushes["rpm_high"] = gfx.CreateSolidBrush(0xb9, 0x57, 0x4a);

    _brushes["split"] = gfx.CreateSolidBrush(245, 117, 66, 100);
    _brushes["transparent"] = gfx.CreateSolidBrush(0, 0, 0, 0);
    -- _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x2c, 0x33, 0x3c, 10);
    _brushes["telemetryBackground"] = gfx.CreateRadialGradientBrush(
        helper.getColor(0x2c, 0x33, 0x3c, 255),
        helper.getColor(0x2c, 0x33, 0x3c, 230),
        helper.getColor(0x2c, 0x33, 0x3c, 215),
        helper.getColor(0x2c, 0x33, 0x3c, 10));
    -- _brushes["rpm"] = gfx.CreateSolidBrush(0x9c, 0x9e, 0x5c, 255)
    _brushes["rpm"] = gfx.CreateLinearGradientBrush(
        -- green
        helper.getColor(0x31, 0xd2, 0x1b, 255),
        -- yellow
        helper.getColor(0xd3, 0xec, 0x46, 255),
        -- red
        helper.getColor(0xfb, 0x21, 0x0d, 255)
    );
    _brushes["clutch"] = gfx.CreateSolidBrush(210, 186, 27, 255)
    
    local _fonts = {};
    _fonts["rpm"] = gfx.CreateCustomFont("WRC Clean Roman", 12);
    _fonts["speed"] = gfx.CreateCustomFont("WRC Clean Roman", 32);
    _fonts["label"] = gfx.CreateCustomFont("WRC Clean Roman", 24);
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function drawStaticFrames(gfx, data, helper, i18n, x, y, width, height) 
    -- print("drawing the static frames")
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    -- draw the static frames
    -- 1. RPM text
    gfx.FillRectangle(_brushes["green"], x, y, x + 1/6 * width, y + 1/8 * height)
    gfx.drawTextWithBackgroundCentered(_fonts["rpm"], 1/8 * height * fontFactor, _brushes["black"], _brushes["transparent"], x + 1/12 * width, y + 1/16 * height, i18n.ResolveByKey("dashboard.rpm"));
    gfx.FillRectangle(_brushes["black"], x + 1/6 * width, y, x + width, y + 1/8 * height);

    -- 2. bottom bar
    gfx.FillRectangle(_brushes["black"], x, y + 3/4 * height, x + width, y + height);
end

function drawSplitLines(gfx, x, y, width, height)
    local _brushes = resources["brushes"];

    -- 3. split lines
    gfx.DrawLine(_brushes["split"], x + 1/3 * width, y + 3/4 * height, x + 1/3 * width, y + height, 4);
    gfx.DrawLine(_brushes["split"], x + 2/3 * width, y + 1/8 * height, x + 2/3 * width, y + height, 4);
    gfx.DrawLine(_brushes["split"], x + 5/6 * width, y + 1/8 * height, x + 5/6 * width, y + 3/4 * height, 4);
end

function drawRPM(gfx, data, conf, helper, x, y, width, height)
    -- rpm here won't blink
    framesCount = framesCount + 1;
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local rpm = data.RPM;
    local maxRPM = data.MaxRPM
    if (rpm > 0) then
        -- RPM color should become from green to yellow and then red
        -- use linear gradient brush
        _brushes["rpm"].SetRange(x + 1/6 * width - 1, y, x + width, y);

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
        else
            bInBlink = true;
        end

        gfx.FillRectangle(_brushes["rpm"], x + 1/6 * width - 1, y, x + 1/6 * width + rpm/maxRPM * 5/6 * width, y + 1/8 * height);
    end

    -- draw rpm numbers from 0 to maxRPM, every 1000 draw a digit
    for i = 0, maxRPM, 1000 do
        if (i == 0) then
            goto continue;  -- skip 0
        end
        local rpmText = tostring(i / 1000);
        local rpmTextX = x + 1/6 * width + (i / maxRPM) * 5/6 * width;
        local rpmTextY = y + 1/16 * height;
        gfx.drawTextWithBackgroundCentered(_fonts["rpm"], 1/8 * height * fontFactor, _brushes["black"], _brushes["transparent"], rpmTextX, rpmTextY, rpmText);
        ::continue::
    end

end

function drawThrottle(gfx, data, helper, i18n, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local throttle = data.Throttle;
    local throttleText = i18n.ResolveByKey("dashboard.throttle");

    gfx.FillRectangle(_brushes["green"], x + 2/3 * width, y + 3/4 * height, x + 2/3 * width + 1/3 * width * throttle, y + height);

    gfx.drawTextWithBackgroundCentered(_fonts["label"], 1/4 * height * fontFactor, _brushes["white"], _brushes["transparent"], x + 5/6 * width, y + 7/8 * height, throttleText);
end

function drawBrake(gfx, data, helper, i18n, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local brake = data.Brake;
    local brakeText = i18n.ResolveByKey("dashboard.brake");

    gfx.FillRectangle(_brushes["red"], x + 1/3 * width + (1-brake) * 1/3 * width, y + 3/4 * height, x + 2/3 * width, y + height);

    gfx.drawTextWithBackgroundCentered(_fonts["label"], 1/4 * height * fontFactor, _brushes["white"], _brushes["transparent"], x + 1/2 * width, y + 7/8 * height, brakeText);
end

function drawClutch(gfx, data, helper, i18n, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local clutch = data.Clutch;
    local clutchText = i18n.ResolveByKey("dashboard.clutch");

    gfx.FillRectangle(_brushes["clutch"], x, y + 3/4 * height, x + 1/3 * width * clutch, y + height);
    gfx.drawTextWithBackgroundCentered(_fonts["label"], 1/4 * height * fontFactor, _brushes["white"], _brushes["transparent"], x + 1/6 * width, y + 7/8 * height, clutchText);
end

function drawHandBrake(gfx, self, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local handBrake = data.HandBrake;

    -- background
    gfx.FillRectangle(_brushes["white"], x + 5/6 * width, y + 1/8 * height, x + width, y + 3/4 * height);
    
    -- draw icon 49x38
    local iconWHRatio = 1.29;
    local iconWidth = width * 1/6 * fontFactor;
    local iconHeight = iconWidth / iconWHRatio;
    local iconStartX = x + 11/12 * width - iconWidth / 2;
    local iconStartY = y + 7/16 * height - iconHeight / 2;
    if (handBrake > 0) then
        gfx.DrawImage(self.ImageResources["images@handbrake_active"], iconStartX, iconStartY, iconStartX + iconWidth, iconStartY + iconHeight);
    else
        gfx.DrawImage(self.ImageResources["images@handbrake"], iconStartX, iconStartY, iconStartX + iconWidth, iconStartY + iconHeight);
    end
end

function drawSpeed(gfx, data, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local speed = math.floor(data.Speed);
    local speedText = tostring(speed);

    -- background
    gfx.FillRectangle(_brushes["white"], x, y + 1/8 * height, x + 2/3 * width, y + 3/4 * height);
    
    gfx.drawTextWithBackgroundCentered(_fonts["speed"], 5/8 * height * fontFactor, _brushes["black"], _brushes["transparent"], x + 4/18 * width, y + 7/16 * height, speedText);
    gfx.drawTextWithBackgroundCentered(_fonts["speed"], 1/4 * height * fontFactor, _brushes["black"], _brushes["transparent"], x + 1/2 * width, y + 1/2 * height, "Km/h");
end

function drawGear(gfx, data, conf, helper, x, y, width, height)
    -- right bottom
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local gear = math.floor(data.Gear);
    local maxGear = math.floor(data.MaxGears);
    local gearText = getGear(gear);

    local rpmLevel = getRPMLevel(data, conf);

    -- if it's max gear, won't blink
    if (not bInBlink and gear < maxGear) then
        gfx.FillRectangle(_brushes["red"], x + 2/3 * width, y + 1/8 * height, x + 5/6 * width, y + 3/4 * height);
    else    
        gfx.FillRectangle(_brushes["black"], x + 2/3 * width, y + 1/8 * height, x + 5/6 * width, y + 3/4 * height);
    end
    
    if (rpmLevel == 1) then
        gfx.FillRectangle(_brushes["rpm_low"], x + 2/3 * width, y + 1/8 * height, x + 5/6 * width, y + 3/4 * height);
    end

    if (rpmLevel == 2) then
        gfx.FillRectangle(_brushes["rpm_medium"], x + 2/3 * width, y + 1/8 * height, x + 5/6 * width, y + 3/4 * height);
    end

    if (rpmLevel == 3) then
        gfx.FillRectangle(_brushes["rpm_high"], x + 2/3 * width, y + 1/8 * height, x + 5/6 * width, y + 3/4 * height);
    end
    
    gfx.drawTextWithBackgroundCentered(_fonts["label"], 1/6 * width * fontFactor, _brushes["white"], _brushes["transparent"], x + 3/4 * width, y + 7/16 * height, gearText);
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
    
    local telemetryCenterX, telemetryCenterY, width, height = getDashboardPositionCenter(self, gfx, whRatio);

    local telemetryStartX = telemetryCenterX - width / 2;
    local telemetryStartY = telemetryCenterY - height / 2;

    drawStaticFrames(gfx, data, helper, i18n, telemetryStartX, telemetryStartY, width, height);
    drawRPM(gfx, data, conf, helper, telemetryStartX, telemetryStartY, width, height);
    drawThrottle(gfx, data, helper, i18n, telemetryStartX, telemetryStartY, width, height);
    drawBrake(gfx, data, helper, i18n, telemetryStartX, telemetryStartY, width, height);
    drawClutch(gfx, data, helper, i18n, telemetryStartX, telemetryStartY, width, height);
    drawHandBrake(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, height);

    drawSpeed(gfx, data, helper, telemetryStartX, telemetryStartY, width, height);
    drawGear(gfx, data, conf, helper, telemetryStartX, telemetryStartY, width, height);

    drawSplitLines(gfx, telemetryStartX, telemetryStartY, width, height);
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end
