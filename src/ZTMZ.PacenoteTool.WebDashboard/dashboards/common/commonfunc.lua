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
    local rpm, shift, start = finiteNumber(data.RPM), finiteNumber(data.ShiftLightsRPMEnd), finiteNumber(data.ShiftLightsRPMStart);
    if rpm == nil then return 0 end
    -- Use the provider's progressive light range (DR2: last 1000 RPM).
    -- Flash at the shift point to prompt a gear change, not at the rev limit.
    if data.ShiftLightsRPMValid and shift ~= nil and start ~= nil
        and start >= 0 and shift > start then
        local range = shift - start;
        if rpm >= shift then return 4 end
        if rpm >= start + range * 2 / 3 then return 3 end
        if rpm >= start + range / 3 then return 2 end
        if rpm >= start then return 1 end
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

-- Shared by the modern WRC dashboards. Read each frame: a previous car's
-- hybrid state must disappear as soon as the optional snapshot disappears.
function getHybridDisplayState(data)
    local hybrid = data and data.Hybrid;
    if hybrid == nil then return nil end
    local function fraction(value)
        local n = finiteNumber(value);
        if n == nil then return nil end
        return math.max(0, math.min(1, n));
    end
    local battery = fraction(hybrid.BatteryChargeFraction);
    local progress = fraction(hybrid.BoostProgressFraction);
    local map = finiteNumber(hybrid.BoostMap);
    if map == nil or map < 1 or map > 99 or map ~= math.floor(map) then map = nil end
    local state = "hybrid";
    if hybrid.BoostActive == true then state = "boost"
    elseif hybrid.BoostReady == true then state = "ready"
    elseif hybrid.RegenerationActive == true then state = "regen"
    elseif hybrid.BoostActive == false and hybrid.BoostReady == false and hybrid.RegenerationActive == false then state = "standby" end
    if battery == nil and progress == nil and map == nil and state == "hybrid" then return nil end
    return { battery = battery, progress = progress, map = map, state = state };
end

-- Missing/invalid output is unavailable; zero is a valid centered reading.
function getForceFeedbackDisplayState(data)
    local feedback = data and data.ForceFeedback;
    if feedback == nil then return nil end
    -- Fengari exposes explicit JSON null as userdata rather than Lua nil.
    if type(feedback) == "userdata" and tostring(feedback) == "null" then return nil end
    local output = finiteNumber(feedback.OutputFraction);
    if output == nil or output < -1 or output > 1 then return nil end
    return { output = output, magnitude = math.abs(output) };
end

-- x/width bound the rail. The limit brush denotes normalized
-- output at its limit, not physical torque or a claim of hardware clipping.
function drawForceFeedbackRail(gfx, state, font, track, active, limit, background, x, y, width, size)
    if state == nil then return end
    local brush = state.magnitude >= .99 and limit or active;
    local center = x + width * .5;
    local endpoint = center + width * .5 * state.output;
    -- Keep small dashboard scales from reducing the rail to subpixel strokes.
    gfx.DrawLine(track, x, y, x + width, y, math.max(1, size * .18));
    gfx.DrawLine(active, center, y - size * .3, center, y + size * .3, math.max(1, size * .12));
    if state.magnitude > 0 then
        gfx.DrawLine(brush, center, y, endpoint, y, math.max(1, size * .24));
    end
end

-- Classic uses an outer arc centered at twelve o'clock: signed output sweeps
-- left/right through 150 degrees. Segment lines work in every dashboard renderer.
function drawForceFeedbackArc(gfx, state, font, track, active, limit, background, x, y, radius, size)
    if state == nil then return end
    local span=math.pi*5/6;
    local function arc(brush, first, last, thickness)
        local count=math.max(1,math.ceil(math.abs(last-first)*24));
        for i=1,count do
            local a=first+(last-first)*(i-1)/count;
            local b=first+(last-first)*i/count;
            gfx.DrawLine(brush,x+radius*math.sin(a),y-radius*math.cos(a),
                x+radius*math.sin(b),y-radius*math.cos(b),thickness);
        end
    end
    arc(track,-span,span,math.max(1,size*.18));
    if state.magnitude>0 then arc(state.magnitude>=.99 and limit or active,0,span*state.output,math.max(1,size*.30)) end
    gfx.DrawLine(active,x,y-radius-size*.2,x,y-radius+size*.2,math.max(1,size*.12));
end

-- Follow the WRC dashboards' original shallow steering curve. FFB stays on
-- that path, behind the original red steering dot and its fading trail.
function drawForceFeedbackCurve(gfx,state,track,active,limit,x,y,halfWidth,height,size)
    if state == nil then return end
    local function curve(brush,first,last,thickness)
        local count=math.max(1,math.ceil(math.abs(last-first)*24));
        for i=1,count do
            local a=first+(last-first)*(i-1)/count;
            local b=first+(last-first)*i/count;
            gfx.DrawLine(brush,x+halfWidth*a,y+height*a*a,x+halfWidth*b,y+height*b*b,thickness);
        end
    end
    curve(track,-1,1,math.max(1,size*.18));
    if state.magnitude>0 then
        curve(state.magnitude>=.99 and limit or active,0,state.output,math.max(1,size*.24));
    end
end
