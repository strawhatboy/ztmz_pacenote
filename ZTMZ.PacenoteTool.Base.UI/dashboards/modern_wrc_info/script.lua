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
    _brushes["background"] = gfx.CreateSolidBrush(0x1e, 0x2a, 0x3e, 255);
    _brushes["transparent"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
    _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x2c, 0x33, 0x3c, 255);
    _brushes["theme"] = gfx.CreateSolidBrush(0xfc, 0x4a, 0x01, 255);
    
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
    gfx.FillRectangle(_brushes["background"], x, y, x + width, y + height);

    local geo_path = gfx.CreateGeometry();
    geo_path.BeginFigure(helper.getPoint(x, y), true);
    local point_top1 = helper.getPoint(x + height / 2, y);
    geo_path.AddPoint(point_top1);
    geo_path.AddPoint(helper.getPoint(x + height / 4, y + height));
    geo_path.AddPoint(helper.getPoint(x, y + height));
    geo_path.EndFigure();
    geo_path.Close();

    gfx.FillGeometry(geo_path, _brushes["theme"]);

    -- print("drawing the middle frame")
    geo_path = gfx.CreateGeometry();
    geo_path.BeginFigure(helper.getPoint(x + 9 * height / 16, y), true);
    geo_path.AddPoint(helper.getPoint(x + 11 * height / 16, y));
    geo_path.AddPoint(helper.getPoint(x + 7 * height / 16, y + height));
    geo_path.AddPoint(helper.getPoint(x + 5 * height / 16, y + height));
    
    geo_path.EndFigure();
    geo_path.Close();
    gfx.FillGeometry(geo_path, _brushes["theme"]);

    -- print("drawing the right frame")
    geo_path = gfx.CreateGeometry();
    geo_path.BeginFigure(helper.getPoint(x + 7 * width / 10, y), true);
    geo_path.AddPoint(helper.getPoint(x + width, y));
    geo_path.AddPoint(helper.getPoint(x + width, y + height));
    geo_path.AddPoint(helper.getPoint(x + 7 * width / 10 - height / 8, y + height));
    geo_path.EndFigure();
    geo_path.Close();
    gfx.FillGeometry(geo_path, _brushes["theme"]);
end

function drawDriverNameAndRegion(gfx, self, data, ctx, helper, x, y, width, height)
    local driverWeight = 0.5 * height;
    local _fonts = resources["fonts"];
    local _brushes = resources["brushes"];
    local driverName = self.GetConfigByKey("dashboards.settings.drivername");
    local driverRegion = "flags@" .. self.GetConfigByKey("dashboards.settings.driverregion");
    local size = gfx.MeasureString(_fonts["wrc"], driverWeight, driverName);

    gfx.DrawText(_fonts["wrc"], driverWeight, _brushes["white"], x + height, y + height / 2 - size.Y / 2, driverName);


    if (self.ImageResources.ContainsKey(driverRegion)) then
        local regionImage = self.ImageResources[driverRegion];
        local imageWHRatio = regionImage.Width / regionImage.Height;
        local ImageHeight = 3 * height / 4;
        local ImageWidth = ImageHeight * imageWHRatio;
        local startX = x + 7 * width / 10 - height / 2 - ImageWidth;
        local startY = y + height / 2 - ImageHeight / 2;
        gfx.DrawImage(regionImage, startX, startY, startX + ImageWidth, startY + ImageHeight);
    end

    -- stage time
    local stageTime = data.LapTime; -- seconds in float
    local stageTimeStrMinute = math.floor(stageTime / 60);
    local stageTimeStrSecond = math.floor(stageTime % 60);
    local stageTimeMilisecond = math.floor((stageTime - math.floor(stageTime)) * 1000);
    local stageTimeStr = stageTimeStrMinute .. ":" .. stageTimeStrSecond .. "." .. stageTimeMilisecond;
    
    size = gfx.MeasureString(_fonts["wrc"], driverWeight, stageTimeStr);
    gfx.DrawText(_fonts["wrc"], driverWeight, _brushes["white"], x + 7 * width / 10 + (3 * width / 10 - size.X) / 2, y + height / 2 - size.Y / 2, stageTimeStr);
    
    -- stage name
    local stageName = ctx.TrackName;
    size = gfx.MeasureString(_fonts["wrc"], driverWeight * 0.8, stageName);
    local padding = height;

    local geo_path = gfx.CreateGeometry();
    geo_path.BeginFigure(helper.getPoint(x + width - size.X - padding * 2, y - size.Y), true);
    geo_path.AddPoint(helper.getPoint(x + width, y - size.Y));
    geo_path.AddPoint(helper.getPoint(x + width, y));
    geo_path.AddPoint(helper.getPoint(x + width - size.X - padding * 2 - height / 8, y));
    geo_path.EndFigure();
    geo_path.Close();
    gfx.FillGeometry(geo_path, _brushes["background"]);
    
    gfx.DrawText(_fonts["wrc"], driverWeight * 0.8, _brushes["white"], x + width - size.X - padding, y - size.Y, stageName);
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
    local marginH = self.GetConfigByKey("dashboards.settings.marginH") * gfx.Width;
    local marginV = self.GetConfigByKey("dashboards.settings.marginV") * gfx.Height;
    local whRatio = 577.0 / 34.0;

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
    local width = size * whRatio;

    drawStaticFrames(gfx, self, data, helper, telemetryStartX, telemetryStartY, size * whRatio, size);
    drawDriverNameAndRegion(gfx, self, data, ctx, helper, telemetryStartX, telemetryStartY, size * whRatio, size);
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
end
