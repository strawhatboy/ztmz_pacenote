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
        if (rpm >= data.ShiftLightsRPMEnd) then
        -- in EA WRC, this means the shift light is blinking
            rpmLevel = 4;
        end

        if (rpm >= data.ShiftLightsRPMStart and rpm < (data.ShiftLightsRPMStart + 0.5 * rpmLightOnRange)) then
            -- in EA WRC, this means the shift light is on, green
            rpmLevel = 1;
        end

        if (rpm >= (data.ShiftLightsRPMStart + 0.5 * rpmLightOnRange) and rpm < data.ShiftLightsRPMEnd) then
            -- in EA WRC, this means the shift light is on, yellow
            rpmLevel = 2;
        end

        if (rpm < data.ShiftLightsRPMStart) then
            -- in EA WRC, this means the shift light is off
            rpmLevel = 0;
        end

        -- there's no red light in EA WRC, or there should not be red light
    end

    return rpmLevel
end
