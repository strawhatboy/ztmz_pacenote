-- globals without "local" keyword
MAX_SPEED = 200;
MAX_WHEEL_SPEED = 220;
MAX_WHEEL_TEMP = 800;
MAX_G_FORCE = 2.2;
MAX_SUSPENSION_SPD = 1000; -- m/s
MIN_SUSPENSION_SPD = -1000;
MAX_SUSPENSION = 200;
MIN_SUSPENSION = -200;
SWEEPDIRECTION_COUNTERCLOCKWISE = 0;
SWEEPDIRECTION_CLOCKWISE = 1;
ARCSIZE_SMALL = 0;
ARCSIZE_LARGE = 1;
BLINK_INTERVAL_FRAMES_PERCENTAGE = 0.05;

GAMESTATE_Unknown = 0;
GAMESTATE_RaceBegin = 1;
GAMESTATE_CountingDown = 2;
GAMESTATE_Racing = 3;
GAMESTATE_Paused = 4;
GAMESTATE_RaceEnd = 5;
GAMESTATE_AdHocRaceBegin = 6;

function getGameStateName(i18n, gameState)
    local key = nil;
    if gameState == GAMESTATE_Unknown then
        key = "overlay.gameStateUnknown";
    elseif gameState == GAMESTATE_RaceBegin then
        key = "overlay.gameStateRaceBegin";
    elseif gameState == GAMESTATE_CountingDown then
        key = "overlay.gameStateCountingDown";
    elseif gameState == GAMESTATE_Racing then
        key = "overlay.gameStateRacing";
    elseif gameState == GAMESTATE_Paused then
        key = "overlay.gameStatePaused";
    elseif gameState == GAMESTATE_RaceEnd then
        key = "overlay.gameStateRaceEnd";
    elseif gameState == GAMESTATE_AdHocRaceBegin then
        key = "overlay.gameStateAdHocRaceBegin";
    end

    if key ~= nil then
        return i18n.ResolveByKey(key);
    end

    return tostring(gameState);
end

function getRotatePoint(gfx, helper, centerX, centerY, pointX, pointY, angle)
    local point = helper.getPoint(centerX + (pointX - centerX) * math.cos(angle) - (pointY - centerY) * math.sin(angle),
        centerY + (pointX - centerX) * math.sin(angle) + (pointY - centerY) * math.cos(angle));
    return point;
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

    -- need manually dispose this object!
    geo_path.Dispose();
end

-- input: gear: float
-- output: string
function getGear(gear)
    if (gear >= 0) then
        gear = math.floor(gear + 0.5);
    else
        gear = math.ceil(gear - 0.5);
    end

    if (gear == -1 or gear == 10) then
        return "R";
    end
    if (gear == 0) then
        return "N";
    end
    if (gear > 0 and gear < 10) then
        return string.format("%.0f", gear);
    end
    return "";
end

-- Subtracting a non-finite value from itself yields NaN in both CLR-backed
-- NeoLua and native Lua, without relying on boxed numeric equality to huge.
local function finiteNumber(value)
    local number = tonumber(value);
    if number == nil or number - number ~= 0 then return nil end
    return number;
end

-- Startup/menu telemetry may not contain a usable engine RPM range yet.
function getRPMPercentage(data)
    -- NeoLua receives CLR Single fields; browser/native Lua receives numbers.
    -- Normalize both representations before validation and arithmetic.
    local rpm, maximum = finiteNumber(data.RPM), finiteNumber(data.MaxRPM);
    if rpm == nil or maximum == nil
        or maximum <= 0 then
        return 0;
    end
    return finiteNumber(rpm / maximum) or 0;
end

function getRPMLevel(data, conf)
    local rpm, shift, maximum = finiteNumber(data.RPM), finiteNumber(data.ShiftLightsRPMEnd), finiteNumber(data.MaxRPM);
    if rpm == nil then return 0 end
    -- Shift advice and near-limit warning are separate thresholds. Levels are
    -- cumulative: off, green, green+yellow, all steady, red blinking.
    if data.ShiftLightsRPMValid and shift ~= nil
        and shift > 0 then
        if maximum ~= nil
            and maximum >= shift
            and rpm >= math.max(shift, maximum * 0.98) then return 4 end
        if rpm >= shift then return 3 end
        if rpm >= shift * 2 / 3 then return 2 end
        if rpm >= shift / 3 then return 1 end
        return 0;
    end
    local redZoneBlink = conf.HudShiftRedZone;
    local yellowZone = conf.HudShiftYellowZone;
    local greenZone = conf.HudShiftGreenZone;

    local rpmPercentage = getRPMPercentage(data);
    local rpmLevel = 0;
    if (rpmPercentage > redZoneBlink) then
        rpmLevel = 4;
    elseif (rpmPercentage > yellowZone) then
        rpmLevel = 2;
    elseif (rpmPercentage > greenZone) then
        rpmLevel = 1;
    end

    return rpmLevel
end

function getDashboardPositionCenter(self, gfx, whRatio)
    local height = gfx.Height * self.GetConfigByKey("dashboards.settings.size")
    local positionH = self.GetConfigByKey("dashboards.settings.positionH")
    local positionV = self.GetConfigByKey("dashboards.settings.positionV")
    local marginH = self.GetConfigByKey("dashboards.settings.marginH") * gfx.Width;
    local marginV = self.GetConfigByKey("dashboards.settings.marginV") * gfx.Height;
    local width = height * whRatio;
    
    local telemetryCenterX = 0;
    if (positionH == -1) then
        -- -1 means left
        telemetryCenterX = width / 2 + marginH;
    else
        if (positionH == 1) then
            -- 1 means right
            telemetryCenterX = gfx.Width - width / 2 - marginH;
        else
            -- 0 means center
            telemetryCenterX = gfx.Width / 2;
        end
    end

    local telemetryCenterY = 0;
    if (positionV == -1) then
        -- -1 means top
        telemetryCenterY = height / 2 + marginV;
    else
        if (positionV == 1) then
            -- 1 means bottom
            telemetryCenterY = gfx.Height - height / 2 - marginV;
        else
            -- 0 means center
            telemetryCenterY = gfx.Height / 2;
        end
    end

    return telemetryCenterX, telemetryCenterY, width, height;
end

-- top left
function getDashboardPositionStart(self, gfx, whRatio)
    centerX, centerY, width, height = getDashboardPositionCenter(self, gfx, whRatio);
    return centerX - width / 2, centerY - height / 2, width, height;
end

function binarySearch(sortedArray, target)
    local low = 1
    print("get low: " .. low);
    local high = #sortedArray
    print("get high: " .. high);
    
    while low <= high do
        local mid = math.floor((low + high) / 2)
        local current = sortedArray[mid]
        
        if current < target then
            low = mid + 1
        elseif current > target then
            high = mid - 1
        else
            -- Found exact match, return 1-based index
            return mid
        end
    end
    
    -- Not found, return insert position
    return low
end

function getKeysAndValues(t)
    local keys = {}
    local values = {}
    for k, v in pairs(t) do
        table.insert(keys, k)
        table.insert(values, v)
    end
    return keys, values
end

function linearInterpolation(x, x0, x1, y0, y1)
    return y0 + (x - x0) * (y1 - y0) / (x1 - x0);
end

function isStringEmpty(s)
    return s == nil or s == '';
end
