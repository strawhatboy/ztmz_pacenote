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
    local helper = args.GameOverlayDrawingHelper;
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
    _brushes["background"] = gfx.CreateSolidBrush(0, 0, 0, 255);
    _brushes["transparent"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x2c, 0x33, 0x3c, 255);
    _brushes["theme"] = gfx.CreateSolidBrush(0xfc, 0x4a, 0x01, 255);
    _brushes["themeRG"] = gfx.CreateRadialGradientBrush(
        helper.getColor(0xfc, 0x4a, 0x01, 255),
        helper.getColor(0xfc, 0x4a, 0x01, 200),
        helper.getColor(0xfc, 0x4a, 0x01, 0));
    
    local _fonts = {};
    _fonts["consolas"] = gfx.CreateFont("Consolas", 14);
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean Roman", 14);
    _fonts["wrcGear"] = gfx.CreateCustomFont("WRC Clean Roman", 32);
    _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
    _fonts["wrcBold"] = gfx.CreateFont("WRC Clean Bold", 14, "bold");
    
    resources["brushes"] = _brushes;
    resources["fonts"] = _fonts;
end

function drawRacelogicTelemetry(gfx, self, data, ctx, helper, x, y, width, height)
    local _brushes = resources["brushes"];
    local _fonts = resources["fonts"];
    
    -- Add offset and scale factors
    local imageOffsetX = 0  -- Adjust this value to move the image horizontally
    local imageOffsetY = -0  -- Adjust this value to move the image vertically
    local imageScaleX = 1  -- Adjust this value to scale the image width
    local imageScaleY = 1.0  -- Adjust this value to scale the image height

    -- Calculate new dimensions and position
    local imageWidth = width * imageScaleX
    local imageHeight = height * imageScaleY
    local imageX = x + imageOffsetX
    local imageY = y + imageOffsetY

    -- Draw background image with new position and size
    gfx.DrawImage(self.ImageResources["images@background"], x, y, x + width, y + height);
    
    -- Draw background
    -- gfx.FillRectangle(_brushes["background"], x, y, x + width, y + height);

    -- Draw current race time
    local raceTime = data.LapTime; -- seconds in float
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
    
    local multiVal = 0.3;

    local raceTimeStr = raceTimeStrMinute .. ":" .. raceTimeStrSecond .. "." .. raceTimeMilisecond;
    local size = gfx.MeasureString(_fonts["wrcBold"], height * multiVal, raceTimeStr); -- Adjusted height proportion
    -- Adjust character spacing manually
    local charSpacing = -height * 0.031; -- Adjust this value to change spacing
    local xPos = x + height * 0.35;
    for i = 1, #raceTimeStr do
        local char = raceTimeStr:sub(i, i);
        gfx.DrawText(_fonts["wrc"], height * multiVal, _brushes["white"], xPos, y + (height - size.Y) / 2, char); -- Adjusted height proportion
        local charSize = gfx.MeasureString(_fonts["wrc"], height * multiVal, char);
        xPos = xPos + charSize.X + charSpacing;
    end

    -- Draw track length
    local trackLength = string.format("%04.1f km", math.floor(data.TrackLength) / 1000);
    size = gfx.MeasureString(_fonts["wrc"], height * multiVal/2, trackLength);
    gfx.DrawText(_fonts["wrc"], height * multiVal/5, _brushes["white"], x + width - size.X - height * 0.43, y + height * 0.4, "Leng.");
    gfx.DrawText(_fonts["wrc"], height * multiVal/3, _brushes["white"], x + width - size.X - height * 0.3, y + height * 0.38, trackLength);

    -- Draw remaining distance
    local remainingDistance = string.format("%04.1f km", math.floor(data.LapDistance) / 1000);
    size = gfx.MeasureString(_fonts["wrc"], height * multiVal/2, remainingDistance);
    gfx.DrawText(_fonts["wrc"], height * multiVal/5, _brushes["white"], x + width - size.X - height * 0.43, y + height * 0.55, "Dist. ");
    gfx.DrawText(_fonts["wrc"], height * multiVal/3, _brushes["white"], x + width - size.X - height * 0.3, y + height * 0.52, remainingDistance);
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
    
    -- calculate the margin, padding, pos of each element
    local size = gfx.Height * self.GetConfigByKey("dashboards.settings.size") / 4; -- height is halved
    local positionH = self.GetConfigByKey("dashboards.settings.positionH");
    local positionV = self.GetConfigByKey("dashboards.settings.positionV");
    local marginH = self.GetConfigByKey("dashboards.settings.marginH") * gfx.Width;
    local marginV = self.GetConfigByKey("dashboards.settings.marginV") * gfx.Height;
    local whRatio = 2.62; --self.GetConfigByKey("dashboards.settings.whRatio");

    local telemetryStartX = 0;
    if (positionH == -1) then
        telemetryStartX = 0 + marginH;
    elseif (positionH == 1) then
        telemetryStartX = gfx.Width - size * whRatio - marginH;
    else
        telemetryStartX = gfx.Width / 2 - size * whRatio / 2;
    end
    
    local telemetryStartY = 0;
    if (positionV == -1) then
        telemetryStartY = 0 + marginV;
    elseif (positionV == 1) then
        telemetryStartY = gfx.Height - size - marginV;
    else
        telemetryStartY = gfx.Height / 2 - size / 2;
    end
    local width = size * whRatio * 1; -- increase width to fit all elements

    drawRacelogicTelemetry(gfx, self, data, ctx, helper, telemetryStartX, telemetryStartY, width, size);
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end