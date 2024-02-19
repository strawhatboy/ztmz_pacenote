local resources = {};

local lastTime = 0.0;
local recordedData = {};
local currentDataIndex = 0;
local recordedDataLength = 0;
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
    _brushes["border"] = gfx.CreateSolidBrush(0x5d, 0x5b, 0x58, 150);
    _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0, 150);
    _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0, 150);
    _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255, 150);

    _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x2c, 0x33, 0x3c, 100);
    
    local _fonts = {};
    _fonts["wrc"] = gfx.CreateCustomFont("WRC Clean Roman", 14);
    
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
    gfx.FillRectangle(_brushes["telemetryBackground"], x, y, x + width, y + height);
    -- 2. line in the middle
    -- print("drawing the line in the middle")
    gfx.DrawLine(_brushes["border"], x, y + height / 2, x + width, y + height / 2, 1);
end

function drawLines(gfx, self, data, helper, x, y, width, height)
    local lineWeight = self.GetConfigByKey("dashboards.settings.lineWeight");
    local _brushes = resources["brushes"];
    -- resize the recordedData array according to the recordedDataLength
    if (recordedDataLength > #recordedData) then
        for i = #recordedData + 1, recordedDataLength do
            recordedData[i] = { 0, 0, 0 };
        end
    elseif (recordedDataLength < #recordedData) then
        for i = #recordedData, recordedDataLength + 1, -1 do
            table.remove(recordedData, i);
        end
        if (currentDataIndex > recordedDataLength) then
            currentDataIndex = recordedDataLength;
        end
    end

    -- 60fps
    if (data.LapTime - lastTime >= 0.016) then
        lastTime = data.LapTime;
        currentDataIndex = currentDataIndex + 1;
        if (currentDataIndex > recordedDataLength) then
            currentDataIndex = 1;
        end
        recordedData[currentDataIndex] = { data.Throttle, data.Brake, data.Clutch };
    elseif (data.LapTime - lastTime < 0) then
        lastTime = data.LapTime;
    end

    -- print("drawing the lines: " .. currentDataIndex .. " " .. recordedDataLength)
    -- draw the lines
    local step = width / recordedDataLength;
    local lastX = x;
    local geoThrottle = gfx.CreateGeometry();
    local geoBrake = gfx.CreateGeometry();
    local geoClutch = gfx.CreateGeometry();

    local isFirst = true;

    for i = currentDataIndex + 1, currentDataIndex + recordedDataLength do
        if (i > recordedDataLength) then
            i = i - recordedDataLength;
        end

        -- print("drawing the lines: " .. i)
        local throttle = recordedData[i][1];
        local brake = recordedData[i][2];
        local clutch = recordedData[i][3];
        local throttleY = y + height - throttle * height;
        local brakeY = y + height - brake * height;
        local clutchY = y + height - clutch * height;

        -- print("drawing the lines: " .. throttleY .. " " .. brakeY .. " " .. clutchY)
        if (isFirst) then
            isFirst = false;
            geoThrottle.BeginFigure(helper.getPoint(lastX, throttleY), false);
            geoBrake.BeginFigure(helper.getPoint(lastX, brakeY), false);
            geoClutch.BeginFigure(helper.getPoint(lastX, clutchY), false);
        else
            geoThrottle.AddPoint(helper.getPoint(lastX, throttleY));
            geoBrake.AddPoint(helper.getPoint(lastX, brakeY));
            geoClutch.AddPoint(helper.getPoint(lastX, clutchY));
        end

        lastX = lastX + step;
    end

    geoThrottle.EndFigure(false);
    geoThrottle.Close();
    geoBrake.EndFigure(false);
    geoBrake.Close();
    geoClutch.EndFigure(false);
    geoClutch.Close();

    gfx.DrawGeometry(geoThrottle, _brushes["green"], lineWeight);
    gfx.DrawGeometry(geoBrake, _brushes["red"], lineWeight);
    gfx.DrawGeometry(geoClutch, _brushes["blue"], lineWeight);
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
    local whRatio = self.GetConfigByKey("dashboards.settings.whRatio");
    recordedDataLength = math.floor(self.GetConfigByKey("dashboards.settings.recordedFrames"));
    local paddingH = self.GetConfigByKey("dashboards.settings.paddingH");
    local paddingV = self.GetConfigByKey("dashboards.settings.paddingV");

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

    drawStaticFrames(gfx, self, data, helper, telemetryStartX, telemetryStartY, width, size);
    drawLines(gfx, self, data, helper, telemetryStartX + width * paddingH, telemetryStartY + size * paddingV, width * (1 - 2 * paddingH), size * (1 - 2 * paddingV));
end

function onExit()
    for k,v in ipairs(resources["brushes"]) do
        v.Dispose();
    end
    for k,v in ipairs(resources["fonts"]) do
        v.Dispose();
    end
end
