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

    _brushes["rpm_low"] = gfx.CreateSolidBrush(0x60, 0x9d, 0x51);
    _brushes["rpm_medium"] = gfx.CreateSolidBrush(0xd3, 0xa9, 0x5b);
    _brushes["rpm_high"] = gfx.CreateSolidBrush(0xb9, 0x57, 0x4a);
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean Roman", 14);
    _fonts["wrcGear"] = gfx.CreateCustomFont("WRC Clean Roman", 32);
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
    -- print("draw the RPM")
    framesCount = framesCount + 1;
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    local rpmLevel = getRPMLevel(data);
    
    if (rpmLevel > 0) then
        gfx.FillRectangle(_brushes["rpm_low"], x, y, x + width / 6, y + height);
        gfx.FillRectangle(_brushes["rpm_low"], x + 5 * width / 6, y, x + width, y + height);
    end

    if (rpmLevel > 1) then
        gfx.FillRectangle(_brushes["rpm_medium"], x + width / 6, y, x + 2 * width / 6, y + height);
        gfx.FillRectangle(_brushes["rpm_medium"], x + 4 * width / 6, y, x + 5 * width / 6, y + height);
    end

    -- data.ShiftLightsRPMValid is from EA WRC game, in RBR, it's complex to get the shift light
    if (rpmLevel > 2 and rpmLevel < 4) then
        gfx.FillRectangle(_brushes["rpm_high"], x + 2 * width / 6, y, x + 3 * width / 6, y + height);
        gfx.FillRectangle(_brushes["rpm_high"], x + 3 * width / 6, y, x + 4 * width / 6, y + height);
    end

    if (rpmLevel > 3) then
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
            gfx.FillRectangle(_brushes["rpm_high"], x + 2 * width / 6, y, x + 3 * width / 6, y + height);
            gfx.FillRectangle(_brushes["rpm_high"], x + 3 * width / 6, y, x + 4 * width / 6, y + height);
        end
    end
end

function drawGear(gfx, data, conf, helper, x, y, width, height) 
    -- print("draw the gear")
    -- right bottom
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    local gear = data.Gear;
    local gearText = getGear(gear);

    gfx.drawTextWithBackgroundCentered(_fonts["wrcGear"], height * 0.8, _brushes["white"], _brushes["transparent"], x + width / 2, y + height / 2, gearText);
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

    drawStaticFrames(gfx, data, conf, helper, telemetryStartX, telemetryStartY, size * whRatio, size);
    drawRPM(gfx, data, conf, helper, telemetryStartX, telemetryStartY, size * whRatio, size);
    drawGear(gfx, data, conf, helper, telemetryStartX, telemetryStartY, size * whRatio, size);
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end
