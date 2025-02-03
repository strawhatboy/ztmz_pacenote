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
    if (gear == -1 or gear == 10) then
        return "R";
    end
    if (gear == 0) then
        return "N";
    end
    if (gear > 0 and gear < 10) then
        return tostring(gear);
    end
    return "";
end

function getRPMLevel(data, conf)
    local redZoneBlink = conf.HudShiftRedZone;
    local redZone = 0.7;
    local yellowZone = conf.HudShiftYellowZone;
    local greenZone = conf.HudShiftGreenZone;

    local rpm = data.RPM;
    local maxRPM = data.MaxRPM;
    local rpmPercentage = rpm / maxRPM;
    local rpmLevel = 0;
    if (rpmPercentage > redZoneBlink) then
        rpmLevel = 4;
    elseif (rpmPercentage > yellowZone) then
        rpmLevel = 2;
    elseif (rpmPercentage > greenZone) then
        rpmLevel = 1;
    end

    if (data.ShiftLightsRPMValid) then
        -- EA WRC Only
        rpmLightOnRange = data.ShiftLightsRPMEnd - data.ShiftLightsRPMStart;
        if (rpm >= (data.ShiftLightsRPMStart + 1 * rpmLightOnRange)) then
        -- in EA WRC, this means the shift light is blinking
        -- we blink if the rpm is very close to the ShiftLightsRPMEnd
            rpmLevel = 4;
        end

        if (rpm >= data.ShiftLightsRPMStart and rpm < (data.ShiftLightsRPMStart + 0.33333 * rpmLightOnRange)) then
            -- in EA WRC, this means the shift light is on, green
            rpmLevel = 1;
        end

        if (rpm >= (data.ShiftLightsRPMStart + 0.33333 * rpmLightOnRange) and rpm < (data.ShiftLightsRPMStart + 0.66667 * rpmLightOnRange)) then
            -- in EA WRC, this means the shift light is on, red
            rpmLevel = 2;
        end

        if (rpm >= (data.ShiftLightsRPMStart + 0.66667 * rpmLightOnRange) and rpm < (data.ShiftLightsRPMStart + 1 * rpmLightOnRange)) then
            -- in EA WRC, this means the shift light is on, red
            rpmLevel = 3;
        end

        if (rpm < data.ShiftLightsRPMStart) then
            -- in EA WRC, this means the shift light is off
            rpmLevel = 0;
        end

        -- there's no red light in EA WRC, or there should not be red light
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
