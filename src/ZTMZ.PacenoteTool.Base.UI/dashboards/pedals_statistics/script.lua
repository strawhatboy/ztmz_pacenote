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
    _brushes["yellow"] = gfx.CreateSolidBrush(255, 255, 0, 150);

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
    local showThrottlePedal = self.GetConfigByKey("dashboards.settings.showThrottlePedal");
    local showBrakePedal = self.GetConfigByKey("dashboards.settings.showBrakePedal");
    local showClutchPedal = self.GetConfigByKey("dashboards.settings.showClutchPedal");
    local showHandBrakePedal = self.GetConfigByKey("dashboards.settings.showHandBrakePedal");
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
        recordedData[currentDataIndex] = { data.Throttle, data.Brake, data.Clutch, data.HandBrake };
    elseif (data.LapTime - lastTime < 0) then
        lastTime = data.LapTime;
    end

    -- print("drawing the lines: " .. currentDataIndex .. " " .. recordedDataLength)
    -- draw the lines
    local step = width / recordedDataLength;
    local lastX = x;
    -- local geoThrottle = gfx.CreateGeometry();
    -- local geoBrake = gfx.CreateGeometry();
    -- local geoClutch = gfx.CreateGeometry();
    -- local geoHandBrake = gfx.CreateGeometry();
    -- change from geometry to lines

    local isFirst = true;

    local throttle = 0;
    local brake = 0;
    local clutch = 0;
    local handBrake = 0;
    local throttleY = 0;
    local brakeY = 0;
    local clutchY = 0;
    local handBrakeY = 0;
    
    local lastThrottleY = throttleY;
    local lastBrakeY = brakeY;
    local lastClutchY = clutchY;
    local lastHandBrakeY = handBrakeY;

    for i = currentDataIndex + 1, currentDataIndex + recordedDataLength do
        if (i > recordedDataLength) then
            i = i - recordedDataLength;
        end

        -- print("drawing the lines: " .. i)
        throttle = recordedData[i][1];
        brake = recordedData[i][2];
        clutch = recordedData[i][3];
        handBrake = recordedData[i][4];
        if (throttle < 0) then
            throttle = 0;
        elseif (throttle > 1) then
            throttle = 1;
        end

        if (brake < 0) then
            brake = 0;
        elseif (brake > 1) then
            brake = 1;
        end

        if (clutch < 0) then
            clutch = 0;
        elseif (clutch > 1) then
            clutch = 1;
        end

        if (handBrake < 0) then
            handBrake = 0;
        elseif (handBrake > 1) then
            handBrake = 1;
        end
        throttleY = y + height - throttle * height;
        brakeY = y + height - brake * height;
        clutchY = y + height - clutch * height;
        handBrakeY = y + height - handBrake * height;

        -- print("drawing the lines: " .. throttleY .. " " .. brakeY .. " " .. clutchY)
        if (isFirst) then
            isFirst = false;
            -- geoThrottle.BeginFigure(helper.getPoint(lastX, throttleY), false);
            -- geoBrake.BeginFigure(helper.getPoint(lastX, brakeY), false);
            -- geoClutch.BeginFigure(helper.getPoint(lastX, clutchY), false);
            -- geoHandBrake.BeginFigure(helper.getPoint(lastX, handBrakeY), false);
        else
            -- geoThrottle.AddPoint(helper.getPoint(lastX, throttleY));
            -- geoBrake.AddPoint(helper.getPoint(lastX, brakeY));
            -- geoClutch.AddPoint(helper.getPoint(lastX, clutchY));
            -- geoHandBrake.AddPoint(helper.getPoint(lastX, handBrakeY));
            if (showThrottlePedal) then
                gfx.DrawLine(_brushes["green"], lastX, lastThrottleY, lastX + step, throttleY, lineWeight);
            end
            if (showBrakePedal) then
                gfx.DrawLine(_brushes["red"], lastX, lastBrakeY, lastX + step, brakeY, lineWeight);
            end
            if (showClutchPedal) then
                gfx.DrawLine(_brushes["blue"], lastX, lastClutchY, lastX + step, clutchY, lineWeight);
            end
            if (showHandBrakePedal and data.HandBrakeValid) then
                gfx.DrawLine(_brushes["yellow"], lastX, lastHandBrakeY, lastX + step, handBrakeY, lineWeight);
            end
        end

        lastX = lastX + step;
        lastThrottleY = throttleY;
        lastBrakeY = brakeY;
        lastClutchY = clutchY;
        lastHandBrakeY = handBrakeY;
    end

    -- geoThrottle.EndFigure(false);
    -- geoThrottle.Close();
    -- geoBrake.EndFigure(false);
    -- geoBrake.Close();
    -- geoClutch.EndFigure(false);
    -- geoClutch.Close();
    -- geoHandBrake.EndFigure(false);
    -- geoHandBrake.Close();

    -- if (showThrottlePedal) then
    --     gfx.DrawGeometry(geoThrottle, _brushes["green"], lineWeight);
    -- end
    
    -- if (showBrakePedal) then
    --     gfx.DrawGeometry(geoBrake, _brushes["red"], lineWeight);
    -- end

    -- if (showClutchPedal) then
    --     gfx.DrawGeometry(geoClutch, _brushes["blue"], lineWeight);
    -- end

    -- if (showHandBrakePedal and data.HandBrakeValid) then    -- handbrake data should be available
    --     gfx.DrawGeometry(geoHandBrake, _brushes["yellow"], lineWeight);
    -- end
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
