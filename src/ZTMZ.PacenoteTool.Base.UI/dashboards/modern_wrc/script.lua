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
    local _brushes = {};
    local helper = args.GameOverlayDrawingHelper;
    _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
    _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0, 200);
    _brushes["rpm_low"] = gfx.CreateSolidBrush(0x60, 0x9d, 0x51);
    _brushes["rpm_medium"] = gfx.CreateSolidBrush(0xd3, 0xa9, 0x5b);
    _brushes["rpm_high"] = gfx.CreateSolidBrush(0xb9, 0x57, 0x4a);
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
    _brushes["line"] = gfx.CreateSolidBrush(255, 255, 255, 100);
    _brushes["background"] = gfx.CreateSolidBrush(0x00, 0x00, 0x00, 100);
    _brushes["transparent"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    -- _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x2c, 0x33, 0x3c, 10);
    _brushes["telemetryBackground"] = gfx.CreateRadialGradientBrush(
        helper.getColor(0x2c, 0x33, 0x3c, 150),
        helper.getColor(0x2c, 0x33, 0x3c, 110),
        helper.getColor(0x2c, 0x33, 0x3c, 80),
        helper.getColor(0x2c, 0x33, 0x3c, 0));
    -- _brushes["rpm"] = gfx.CreateSolidBrush(0x9c, 0x9e, 0x5c, 255)
    _brushes["rpm"] = gfx.CreateLinearGradientBrush(
        -- green
        helper.getColor(0x31, 0xd2, 0x1b, 255),
        -- yellow
        helper.getColor(0xd3, 0xec, 0x46, 255),
        -- red
        helper.getColor(0xfb, 0x21, 0x0d, 255)
    );
    _brushes["brake"] = gfx.CreateLinearGradientBrush(
        helper.getColor(0x7a, 0x27, 0x2a, 255),
        helper.getColor(0xd2, 0x18, 0x1d, 255)
    );
    _brushes["throttle"] = gfx.CreateSolidBrush(0xc3, 0xe1, 0x67, 255)
    _brushes["clutch"] = gfx.CreateSolidBrush(0x2a, 0xb4, 0x5d, 255)
    _brushes["hybrid"] = gfx.CreateLinearGradientBrush(
        helper.getColor(0x15, 0xa2, 0xae, 255),
        helper.getColor(0x3a, 0xe3, 0xf2, 255)
    );
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean Roman", 14);
    _fonts["wrcGear"] = gfx.CreateCustomFont("WRC Clean Roman", 32);
    _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function drawStaticFrames(gfx, data, helper, x, y, radius, padding) 
    -- print("drawing the static frames")
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    -- draw the static frames
    -- 1. background
    -- print("drawing the background")
    -- for alpha=10,100,10 do
    --     gfx.FillCircle(_brushes["telemetryBackground"], x, y, (radius - padding * alpha / 100));
    -- end

    -- use RadialGradientBrush
    _brushes["telemetryBackground"].SetCenter(x, y);
    _brushes["telemetryBackground"].SetRadius(radius, radius);
    gfx.FillCircle(_brushes["telemetryBackground"], x, y, radius);

    -- 2. border
    -- print("drawing the border")ClrEnabled = false
    -- gfx.FillCircle(_brushes["border"], x, y, radius - padding);

    -- 3. telemetryBackground
    -- -- print("drawing the telemetryBackground")
    local telemetryRadius = radius - padding - radius * 0.02;
    -- gfx.FillCircle(_brushes["telemetryBackground"], x, y, telemetryRadius);

    -- 4. arc separating brake and throttle
    -- from -pi/6 to 7pi/6
    -- print("drawing the arc separating brake and throttle")
    local radius1stArc = radius - padding - radius * 0.16;
    drawGeo(gfx, helper, x, y, 7.0 * math.pi / 6.0, (2.0 - 1.0 / 6.0) * math.pi, radius1stArc, radius1stArc - radius * 0.01, ARCSIZE_LARGE, _brushes["line"]);

    -- 5. arc separating brake and speed
    -- from -pi/6 to 7pi/6
    -- print("drawing the arc separating brake and speed")
    local radius2ndArc = radius - padding - radius * 0.24;
    drawGeo(gfx, helper, x, y, 7.0 * math.pi / 6.0, -math.pi / 6.0, radius2ndArc, radius2ndArc - radius * 0.01, ARCSIZE_LARGE, _brushes["line"]);

    local lineWeight = radius * 0.01;

    gfx.DrawLine(_brushes["line"], x + radius2ndArc * math.cos(-math.pi / 6), y - radius2ndArc * math.sin(-math.pi / 6),
        x + telemetryRadius * math.cos(-math.pi / 6), y - telemetryRadius * math.sin(-math.pi / 6), lineWeight);
    gfx.DrawLine(_brushes["line"], x + radius2ndArc * math.cos(7 * math.pi / 6), y - radius2ndArc * math.sin(7 * math.pi / 6),
        x + telemetryRadius * math.cos(7 * math.pi / 6), y - telemetryRadius * math.sin(7 * math.pi / 6), lineWeight);
    
    -- vertical line
    gfx.DrawLine(_brushes["line"], x, y - radius1stArc, x, y - telemetryRadius + radius * 0.03, lineWeight);
    gfx.DrawLine(_brushes["line"], x + radius2ndArc * math.cos(-math.pi / 6), y - radius2ndArc * math.sin(-math.pi / 6),
        x + radius2ndArc * math.cos(-math.pi / 6) - radius * 0.2, y - radius2ndArc * math.sin(-math.pi / 6), lineWeight);
    gfx.DrawLine(_brushes["line"], x + radius2ndArc * math.cos(7 * math.pi / 6), y - radius2ndArc * math.sin(7 * math.pi / 6),
        x + radius2ndArc * math.cos(7 * math.pi / 6) + radius * 0.2, y - radius2ndArc * math.sin(7 * math.pi / 6), lineWeight);

    gfx.DrawLine(_brushes["line"], x + radius2ndArc * math.cos(5 * math.pi / 6), y - radius2ndArc * math.sin(5 * math.pi / 6),
        x + radius1stArc * math.cos(5 * math.pi / 6), y - radius1stArc * math.sin(5 * math.pi / 6), lineWeight);
    
    gfx.drawTextWithBackgroundCentered(_fonts["wrc"], radius * 0.10, _brushes["white"], _brushes["transparent"], x, y - radius2ndArc * math.sin(-math.pi / 6), " Km/h ");
end

function drawRPM(gfx, data, conf, helper, x, y, radius, padding)
    framesCount = framesCount + 1;
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local telemetryRadius = radius - padding - radius * 0.03;
    local rpmWeight = radius * 0.02;
    local rpm = data.RPM;
    local maxRPM = data.MaxRPM
    if (rpm > 0) then
        local arcAngle = (4 * math.pi / 3) * rpm / maxRPM;
        local arcSize = ARCSIZE_SMALL;
        if (arcAngle > math.pi) then
            arcSize = ARCSIZE_LARGE;
        end
        -- RPM color should become from green to yellow and then red
        -- use linear gradient brush
        _brushes["rpm"].SetRange(x - telemetryRadius, y, x + telemetryRadius, y);

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
                drawGeo(gfx, helper, x, y, 7 * math.pi / 6, 7 * math.pi / 6 - arcAngle, telemetryRadius, telemetryRadius - rpmWeight, arcSize, _brushes["rpm"]);
            end
        else
            bInBlink = true;
            drawGeo(gfx, helper, x, y, 7 * math.pi / 6, 7 * math.pi / 6 - arcAngle, telemetryRadius, telemetryRadius - rpmWeight, arcSize, _brushes["rpm"]);
        end
    end
end

function drawThrottle(gfx, data, helper, x, y, radius, padding)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local telemetryRadius = radius - padding - radius * 0.06;
    local throttleWeight = radius * 0.08;
    local throttle = data.Throttle;
    if (throttle > 0) then
        local arcAngle = (2 * math.pi / 3) * throttle;
        local arcSize = ARCSIZE_SMALL;
        if (arcAngle > math.pi) then
            arcSize = ARCSIZE_LARGE;
        end
        drawGeo(gfx, helper, x, y, -math.pi / 6 + arcAngle, -math.pi / 6, telemetryRadius, telemetryRadius - throttleWeight, arcSize, _brushes["throttle"]);
    end
end

function drawBrake(gfx, data, helper, x, y, radius, padding)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local telemetryRadius = radius - padding - radius * 0.06;
    local brakeWeight = radius * 0.08;
    local brake = data.Brake;
    if (brake > 0) then
        local arcAngle = (2 * math.pi / 3) * brake;
        local arcSize = ARCSIZE_SMALL;
        if (arcAngle > math.pi) then
            arcSize = ARCSIZE_LARGE;
        end
        _brushes["brake"].SetRange(
            x + telemetryRadius * math.cos(7 * math.pi / 6),
            y - telemetryRadius * math.sin(7 * math.pi / 6),
            x,
            y - telemetryRadius
        );
        drawGeo(gfx, helper, x, y, 7 * math.pi / 6, 7 * math.pi / 6 - arcAngle, telemetryRadius, telemetryRadius - brakeWeight, arcSize, _brushes["brake"]);
    end
end

function drawClutch(gfx, data, helper, x, y, radius, padding)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local telemetryRadius = radius - padding - radius * 0.19;
    local clutchWeight = radius * 0.04;
    local clutch = data.Clutch;
    if (clutch > 0) then
        local arcAngle = (1 * math.pi / 3) * clutch;
        local arcSize = ARCSIZE_SMALL;
        if (arcAngle > math.pi) then
            arcSize = ARCSIZE_LARGE;
        end
        drawGeo(gfx, helper, x, y, 7 * math.pi / 6, 7 * math.pi / 6 - arcAngle, telemetryRadius, telemetryRadius - clutchWeight, arcSize, _brushes["clutch"]);
    end
end

function drawHandBrake(gfx, data, helper, x, y, radius, padding)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local telemetryRadius = radius - padding - radius * 0.19;
    local handBrakeWeight = radius * 0.04;
    local handBrake = data.HandBrake;
    if (handBrake > 0) then
        local arcAngle = math.pi * handBrake;
        local arcSize = ARCSIZE_SMALL;
        if (arcAngle > math.pi) then
            arcSize = ARCSIZE_LARGE;
        end
        _brushes["hybrid"].SetRange(
            x + telemetryRadius * math.cos(-math.pi / 6),
            y - telemetryRadius * math.sin(-math.pi / 6),
            x + telemetryRadius * math.cos(-math.pi / 6 + math.pi),
            y - telemetryRadius * math.sin(-math.pi / 6 + math.pi)
        );
        drawGeo(gfx, helper, x, y, -math.pi / 6 + arcAngle, -math.pi / 6, telemetryRadius, telemetryRadius - handBrakeWeight, arcSize, _brushes["hybrid"]);
    end
end

function drawSpeed(gfx, data, conf, helper, x, y, radius, padding, switchGearNSpeed)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local telemetryRadius = radius - padding - radius * 0.03;
    local speedWeight = (radius - padding) * 0.56;
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
    if (not bInBlink and switchGearNSpeed and speed < maxGear ) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["red"], x, y - telemetryRadius / 6, speedText);
    else
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["transparent"], x, y - telemetryRadius / 6, speedText);
    end

    if (rpmLevel == 1 and switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["rpm_low"], x, y - telemetryRadius / 6, speedText);
    end

    if (rpmLevel == 2 and switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["rpm_medium"], x, y - telemetryRadius / 6, speedText);
    end

    if (rpmLevel == 3 and switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrc"], speedWeight, _brushes["white"], _brushes["rpm_high"], x, y - telemetryRadius / 6, speedText);
    end
end

function drawGear(gfx, data, conf, helper, x, y, radius, padding, switchGearNSpeed)
    -- right bottom
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local telemetryRadius = radius - padding - radius * 0.03;
    local gearWeight = (radius - padding) * 0.35;
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
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["red"], x + telemetryRadius * 0.55, y + telemetryRadius * 0.55, gearText);
    else
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["transparent"], x + telemetryRadius * 0.55, y + telemetryRadius * 0.55, gearText);
    end
    
    if (rpmLevel == 1 and not switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["rpm_low"], x + telemetryRadius * 0.55, y + telemetryRadius * 0.55, gearText);
    end

    if (rpmLevel == 2 and not switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["rpm_medium"], x + telemetryRadius * 0.55, y + telemetryRadius * 0.55, gearText);
    end

    if (rpmLevel == 3 and not switchGearNSpeed) then
        gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], gearWeight, _brushes["white"], _brushes["rpm_high"], x + telemetryRadius * 0.55, y + telemetryRadius * 0.55, gearText);
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
    
    local padding = size * self.GetConfigByKey("dashboards.settings.padding")
    local switchGearNSpeed = self.GetConfigByKey("dashboards.settings.switchGearNSpeed")

    local telemetryCenterX, telemetryCenterY, width, height = getDashboardPositionCenter(self, gfx, 1.0);
    local radius = height / 2;

    drawStaticFrames(gfx, data, helper, telemetryCenterX, telemetryCenterY, radius, padding);
    drawRPM(gfx, data, conf, helper, telemetryCenterX, telemetryCenterY, radius, padding);
    drawThrottle(gfx, data, helper, telemetryCenterX, telemetryCenterY, radius, padding);
    drawBrake(gfx, data, helper, telemetryCenterX, telemetryCenterY, radius, padding);
    drawClutch(gfx, data, helper, telemetryCenterX, telemetryCenterY, radius, padding);
    drawHandBrake(gfx, data, helper, telemetryCenterX, telemetryCenterY, radius, padding);

    drawSpeed(gfx, data, conf, helper, telemetryCenterX, telemetryCenterY, radius, padding, switchGearNSpeed);
    drawGear(gfx, data, conf, helper, telemetryCenterX, telemetryCenterY, radius, padding, switchGearNSpeed);
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end
