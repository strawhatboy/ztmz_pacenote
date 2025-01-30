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
    _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
    _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0);
    _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
    if (conf.HudChromaKeyMode) then
        _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
        _brushes["blue"] = gfx.CreateSolidBrush(255, 0, 255);
        _brushes["orange"] = gfx.CreateSolidBrush(255, 75, 4);
        _brushes["clear"] = gfx.CreateSolidBrush(0, 255, 0);
    else
        _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
        _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
        _brushes["orange"] = gfx.CreateSolidBrush(255, 75, 4);
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

    _brushes["rpm_low"] = gfx.CreateSolidBrush(28, 202, 112);
    _brushes["rpm_medium"] = gfx.CreateSolidBrush(223, 152, 31);
    _brushes["rpm_high"] = gfx.CreateSolidBrush(245, 60, 60);
    -- _brushes["rpm_low"] = gfx.CreateSolidBrush(96, 157, 81);
    -- _brushes["rpm_medium"] = gfx.CreateSolidBrush(211, 169, 91);
    -- _brushes["rpm_high"] = gfx.CreateSolidBrush(185, 87, 74);
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean Roman", 14);
    _fonts["wrcGear"] = gfx.CreateCustomFont("WRC Clean Roman", 32, "bold");
    _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function drawStaticFrames(gfx, data, conf, helper, x, y, width, height) 
    -- 1. draw the background
    -- print("draw the background")
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    gfx.FillRectangle(_brushes["black"], x, y, x + width, y + height);
end

function drawRPM(gfx, data, conf, helper, x, y, width, height) 
    -- 定义宽度变量
    local greenWidth = width / 7.5
    local yellowWidth = width / 7.5

    framesCount = framesCount + 1;
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    local rpmLevel = getRPMLevel(data, conf);
    
    if (rpmLevel > 0) then
        gfx.FillRectangle(_brushes["rpm_low"], x, y, x + greenWidth, y + height);
        gfx.FillRectangle(_brushes["rpm_low"], x + width - greenWidth, y, x + width, y + height);
    end

    if (rpmLevel > 1) then
        gfx.FillRectangle(_brushes["rpm_medium"], x + greenWidth, y, x + greenWidth + yellowWidth, y + height);
        gfx.FillRectangle(_brushes["rpm_medium"], x + width - greenWidth - yellowWidth, y, x + width - greenWidth, y + height);
    end

    if (rpmLevel > 2 and rpmLevel < 4) then
        gfx.FillRectangle(_brushes["rpm_high"], x + greenWidth + yellowWidth, y, x + width - greenWidth - yellowWidth, y + height);
    end

    if (rpmLevel > 3) then
        local framesLimit = BLINK_INTERVAL_FRAMES_PERCENTAGE * gfx.FPS;
        if (framesCount > framesLimit) then
            framesCount = 0;
            bInBlink = not bInBlink;
        end
        if (bInBlink) then
            gfx.FillRectangle(_brushes["rpm_high"], x + greenWidth + yellowWidth, y, x + width - greenWidth - yellowWidth, y + height);
        end
    end
end

function drawGear(gfx, data, conf, helper, x, y, width, height, showAdditionalInfo) 
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local gear = data.Gear;
    local gearText = getGear(gear);

    local gearY = showAdditionalInfo and (y + height / 2.4) or (y + height / 1.9);

    gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], height / 1.2, _brushes["white"], _brushes["transparent"], x + width / 2, gearY, gearText);
end


function drawAdditionalInfo(gfx, data, conf, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local multiVal = 0.15;

    -- Progress bar at the top
    local progressBarHeight = height / 50;
    gfx.FillRectangle(_brushes["orange"], x, y, x + (data.CompletionRate * width), y + progressBarHeight);
    
    -- RPM in the bottom left
    local rpmText = string.format("%04.0f", data.RPM);
    gfx.FillRectangle(_brushes["black"], x, y + height / 1.4, x + width / 3.73, y + height);
    gfx.DrawText(_fonts["wrc"], height * multiVal, _brushes["white"], x + width / 50, y + height / 1.37, rpmText);
    gfx.DrawText(_fonts["wrc"], height * multiVal / 2.4, _brushes["white"], x + width / 10, y + height / 1.12, "RPM");
    
    -- Speed in the bottom right
    local speedText = string.format("%03.0f", data.Speed);
    gfx.FillRectangle(_brushes["black"], x + width / 1.363, y + height / 1.4, x + width , y + height);
    gfx.DrawText(_fonts["wrc"], height * multiVal, _brushes["white"], x + width / 1.28, y + height / 1.37, speedText);
    gfx.DrawText(_fonts["wrc"], height * multiVal / 2.4, _brushes["white"], x + width / 1.22, y + height / 1.12, "KM/H");
    
    -- Lap Time in the bottom center
    local raceTime = data.LapTime;
    local raceTimeStrMinute = math.floor(raceTime / 60);
    local raceTimeStrSecond = math.floor(raceTime % 60);
    local raceTimeMilisecond = math.floor((raceTime - math.floor(raceTime)) * 1000);
    if (raceTimeStrMinute < 10) then
        raceTimeStrMinute = "0" .. raceTimeStrMinute;
    end
    if (raceTimeStrSecond < 10) then
        raceTimeStrSecond = "0" .. raceTimeStrSecond;
    end
    if (raceTimeMilisecond < 10) then
        raceTimeMilisecond = "00" .. raceTimeMilisecond;
    elseif (raceTimeMilisecond < 100) then
        raceTimeMilisecond = "0" .. raceTimeMilisecond;
    end
    
    local lapTimeText = raceTimeStrMinute .. ":" .. raceTimeStrSecond .. "." .. raceTimeMilisecond;
    gfx.FillRectangle(_brushes["black"], x + width / 1.35, y + height / 1.2, x + width / 3.75 , y + height);
    gfx.DrawText(_fonts["wrc"], height * multiVal / 1.6, _brushes["white"], x + width / 2.75, y + height / 1.13, lapTimeText);

    -- Lap Distance in the bottom center
    local LapDistance = (data.TrackLength - data.LapDistance) / 1000; -- convert to km
    local LapDistanceText = string.format("Dist. %.1f km", LapDistance);
    gfx.DrawText(_fonts["wrc"], height * multiVal / 2.6, _brushes["white"], x + width / 2.5, y + height / 1.2, LapDistanceText);

    -- Current Time in the top right
    local currentTime = os.date("%H:%M:%S")
    gfx.DrawText(_fonts["wrc"], height * multiVal / 3, _brushes["white"], x + width / 2.35, y + height / 1.27, currentTime);
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
    
    local size = gfx.Height * self.GetConfigByKey("dashboards.settings.size");
    local positionH = self.GetConfigByKey("dashboards.settings.positionH");
    local positionV = self.GetConfigByKey("dashboards.settings.positionV");
    local whRatio = self.GetConfigByKey("dashboards.settings.whRatio");
    local marginH = self.GetConfigByKey("dashboards.settings.marginH") * gfx.Width;
    local marginV = self.GetConfigByKey("dashboards.settings.marginV") * gfx.Height;

    -- print("calulating the margin, padding, pos of each element")
    
    -- calculate the margin, padding, pos of each element
    ---- print("calculating the margin, padding, pos of each element"); 
    local telemetryStartX = 0;
    if (positionH == -1) then
        -- -1 means left
        telemetryStartX = 0 + marginH;
    else
        if (positionH == 1) then
            -- 1 means right
            telemetryStartX = gfx.Width - size * whRatio - marginH;
        else
            -- 0 means center
            telemetryStartX = gfx.Width / 2 - size * whRatio / 2;
        end
    end
    
    local telemetryStartY = 0;
    if (positionV == -1) then
        -- -1 means top
        telemetryStartY = 0 + marginV;
    else
        if (positionV == 1) then
            -- 1 means bottom
            telemetryStartY = gfx.Height - size - marginV;
        else
            -- 0 means center
            telemetryStartY = gfx.Height / 2 - size / 2;
        end
    end

    local showAdditionalInfo = self.GetConfigByKey("dashboards.settings.showAdditionalInfo");

    drawStaticFrames(gfx, data, conf, helper, telemetryStartX, telemetryStartY, size * whRatio, size);
    drawRPM(gfx, data, conf, helper, telemetryStartX, telemetryStartY, size * whRatio, size);
    drawGear(gfx, data, conf, helper, telemetryStartX, telemetryStartY, size * whRatio, size, showAdditionalInfo);
    
    if showAdditionalInfo then
        drawAdditionalInfo(gfx, data, conf, helper, telemetryStartX, telemetryStartY, size * whRatio, size);
    end
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end
