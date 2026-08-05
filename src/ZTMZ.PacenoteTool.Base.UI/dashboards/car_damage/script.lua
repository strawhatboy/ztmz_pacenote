local resources = {};
local alertState = {};
local whRatio = 0.80;
local trackedDamageFields = {
    "FrontBumperSeverity", "RearBumperSeverity", "RadiatorSeverity", "EngineSeverity", "DifferentialSeverity",
    "LeftDoorSeverity", "RightDoorSeverity",
    "FrontLeftWheelSeverity", "FrontRightWheelSeverity", "RearLeftWheelSeverity", "RearRightWheelSeverity",
    "FrontLeftBrakeSeverity", "FrontRightBrakeSeverity", "RearLeftBrakeSeverity", "RearRightBrakeSeverity",
    "FrontLeftSuspensionSeverity", "FrontRightSuspensionSeverity", "RearLeftSuspensionSeverity", "RearRightSuspensionSeverity",
    "FrontLeftTyreSeverity", "FrontRightTyreSeverity", "RearLeftTyreSeverity", "RearRightTyreSeverity"
};
local aspectRatios = {
    brake = 1.06,
    differential = 0.42,
    engine_gearbox = 1.60,
    front_bumper = 2.27,
    left_door = 1.22,
    radiator_cooling = 1.54,
    rear_bumper = 2.26,
    right_door = 1.20,
    suspension = 0.36,
    tyre = 0.97
};

function onInit(args)
    local gfx = args.Graphics;
    local brushes = {};
    brushes["background"] = gfx.CreateSolidBrush(10, 16, 20, 182);
    brushes["chassis"] = gfx.CreateSolidBrush(47, 63, 70, 54);
    brushes["spine"] = gfx.CreateSolidBrush(123, 144, 153, 44);
    brushes["warning"] = gfx.CreateSolidBrush(246, 188, 68, 128);
    brushes["warningBlinkMuted"] = gfx.CreateSolidBrush(183, 137, 55, 44);
    brushes["warningPersistent"] = gfx.CreateSolidBrush(238, 170, 38, 190);
    brushes["critical"] = gfx.CreateSolidBrush(239, 82, 71, 152);
    brushes["criticalBlinkMuted"] = gfx.CreateSolidBrush(166, 57, 50, 54);
    brushes["criticalPersistent"] = gfx.CreateSolidBrush(225, 60, 54, 204);
    resources["brushes"] = brushes;
    resetAlertState();
end

function resetAlertState()
    alertState.previousSeverities = {};
    alertState.alertUntil = {};
    alertState.lastGameState = nil;
    alertState.lastClock = nil;
end

function getAlertClock(data)
    local clock = data.Time;
    if clock == nil or clock ~= clock or clock < 0 then
        return 0;
    end
    return clock;
end

function enteredRaceStart(context)
    local gameState = context ~= nil and context.GameState or 0;
    local didChange = alertState.lastGameState == nil or alertState.lastGameState ~= gameState;
    alertState.lastGameState = gameState;
    return didChange and (gameState == GAMESTATE_RaceBegin or gameState == GAMESTATE_AdHocRaceBegin);
end

function getDamageSeverities(state)
    return {
        FrontBumperSeverity = state.FrontBumperSeverity,
        RearBumperSeverity = state.RearBumperSeverity,
        RadiatorSeverity = state.RadiatorSeverity,
        EngineSeverity = state.EngineSeverity,
        DifferentialSeverity = state.DifferentialSeverity,
        LeftDoorSeverity = state.LeftDoorSeverity,
        RightDoorSeverity = state.RightDoorSeverity,
        FrontLeftWheelSeverity = state.FrontLeftWheelSeverity,
        FrontRightWheelSeverity = state.FrontRightWheelSeverity,
        RearLeftWheelSeverity = state.RearLeftWheelSeverity,
        RearRightWheelSeverity = state.RearRightWheelSeverity,
        FrontLeftBrakeSeverity = state.FrontLeftBrakeSeverity,
        FrontRightBrakeSeverity = state.FrontRightBrakeSeverity,
        RearLeftBrakeSeverity = state.RearLeftBrakeSeverity,
        RearRightBrakeSeverity = state.RearRightBrakeSeverity,
        FrontLeftSuspensionSeverity = state.FrontLeftSuspensionSeverity,
        FrontRightSuspensionSeverity = state.FrontRightSuspensionSeverity,
        RearLeftSuspensionSeverity = state.RearLeftSuspensionSeverity,
        RearRightSuspensionSeverity = state.RearRightSuspensionSeverity,
        FrontLeftTyreSeverity = state.FrontLeftTyreSeverity,
        FrontRightTyreSeverity = state.FrontRightTyreSeverity,
        RearLeftTyreSeverity = state.RearLeftTyreSeverity,
        RearRightTyreSeverity = state.RearRightTyreSeverity
    };
end

function updateDamageAlerts(data, context, severities, duration)
    local clock = getAlertClock(data);
    local raceStarted = enteredRaceStart(context);
    local clockReset = alertState.lastClock ~= nil and clock + 0.5 < alertState.lastClock;

    for _, field in ipairs(trackedDamageFields) do
        local severity = severities[field] or 0;
        local previousSeverity = alertState.previousSeverities[field];
        if raceStarted or clockReset or (previousSeverity ~= nil and previousSeverity ~= severity) then
            alertState.alertUntil[field] = clock + duration;
        end
        alertState.previousSeverities[field] = severity;
    end

    alertState.lastClock = clock;
    return clock;
end

function isAlertActive(field, clock)
    local alertUntil = alertState.alertUntil[field];
    return alertUntil ~= nil and clock < alertUntil;
end

function getStateBrush(severity, alertActive, clock)
    local brushes = resources["brushes"];
    if severity <= 0 then
        return nil;
    end

    local isCritical = severity >= 3;
    if not alertActive then
        return isCritical and brushes["criticalPersistent"] or brushes["warningPersistent"];
    end

    local blinkOn = math.floor(clock * 2) % 2 == 0;
    if isCritical then
        return blinkOn and brushes["critical"] or brushes["criticalBlinkMuted"];
    end
    return blinkOn and brushes["warning"] or brushes["warningBlinkMuted"];
end

function getImageOpacity(severity, alertActive)
    if alertActive then
        return severity >= 3 and 1.0 or 0.78;
    end

    if severity <= 0 then
        return 0.24;
    end

    return severity >= 3 and 0.72 or 0.64;
end

function drawAsset(gfx, self, key, severity, alertActive, clock, centerX, centerY, height, widthRatio)
    local image = self.ImageResources["images@" .. key];
    local width = height * (widthRatio or aspectRatios[key] or 1);
    local x = centerX - width * 0.5;
    local y = centerY - height * 0.5;
    local stateBrush = getStateBrush(severity, alertActive, clock);
    if stateBrush ~= nil then
        local padding = math.max(2, height * 0.10);
        gfx.FillRoundedRectangle(stateBrush, x - padding, y - padding, x + width + padding, y + height + padding, math.max(2, height * 0.13));
    end
    if image ~= nil then
        gfx.DrawImage(image, x, y, x + width, y + height, getImageOpacity(severity, alertActive));
    end
end

function drawWheel(gfx, self, severity, alertActive, clock, centerX, centerY, height)
    drawAsset(gfx, self, "wheel", severity, alertActive, clock, centerX, centerY, height, 0.50);
end

function drawDetailStack(gfx, self, centerX, top, detailHeight, detailGap, brakeSeverity, brakeAlert, suspensionSeverity, suspensionAlert, tyreSeverity, tyreAlert, clock)
    drawAsset(gfx, self, "brake", brakeSeverity, brakeAlert, clock, centerX, top + detailHeight * 0.5, detailHeight);
    drawAsset(gfx, self, "suspension", suspensionSeverity, suspensionAlert, clock, centerX, top + detailHeight * 1.5 + detailGap, detailHeight);
    drawAsset(gfx, self, "tyre", tyreSeverity, tyreAlert, clock, centerX, top + detailHeight * 2.5 + detailGap * 2, detailHeight);
end

function onUpdate(args)
    local data = args.GameData;
    local damage = data.CarDamage;
    if damage == nil or damage.Dashboard == nil or not damage.Dashboard.IsAvailable then
        return;
    end

    local gfx = args.Graphics;
    local self = args.Self;
    local state = damage.Dashboard;
    local brushes = resources["brushes"];
    local alertDuration = self.GetConfigByKey("dashboards.carDamage.damageAlertDuration") or 10.0;
    local severities = getDamageSeverities(state);
    local alertClock = updateDamageAlerts(data, args.GameContext, severities, alertDuration);
    local x, y, width, height = getDashboardPositionStart(self, gfx, whRatio);
    local centerX = x + width * 0.5;
    local bodyWidth = height * 0.30;
    local bodyLeft = centerX - bodyWidth * 0.5;
    local bodyRight = centerX + bodyWidth * 0.5;
    local bodyTop = y + height * 0.075;
    local bodyBottom = y + height * 0.925;
    local radiatorHeight = height * 0.085;
    local engineHeight = height * 0.115;
    local transmissionHeight = height * 0.18;
    local doorHeight = height * 0.118;
    local wheelHeight = height * 0.13;
    local frontWheelY = y + height * 0.29;
    local rearWheelY = y + height * 0.71;
    local detailHeight = height * 0.048;
    local detailGap = height * 0.008;
    local detailStackHeight = detailHeight * 3 + detailGap * 2;
    local frontDetailTop = frontWheelY - detailStackHeight * 0.5;
    local rearDetailTop = rearWheelY - detailStackHeight * 0.5;
    local leftDetailX = centerX - height * 0.095;
    local rightDetailX = centerX + height * 0.095;
    local leftWheelX = bodyLeft - height * 0.07;
    local rightWheelX = bodyRight + height * 0.07;
    local leftDoorX = centerX - height * 0.15;
    local rightDoorX = centerX + height * 0.15;
    local doorY = y + height * 0.55;

    gfx.FillRoundedRectangle(brushes["background"], x, y, x + width, y + height, height * 0.05);

    -- Keep a quiet drivetrain guide so the highlighted component is the primary signal.
    gfx.FillRoundedRectangle(brushes["chassis"], bodyLeft, bodyTop, bodyRight, bodyBottom, height * 0.06);
    gfx.DrawLine(brushes["spine"], centerX, bodyTop + height * 0.10, centerX, bodyBottom - height * 0.10, height * 0.005);
    gfx.DrawLine(brushes["spine"], leftWheelX, frontWheelY, rightWheelX, frontWheelY, height * 0.004);
    gfx.DrawLine(brushes["spine"], leftWheelX, rearWheelY, rightWheelX, rearWheelY, height * 0.004);

    drawAsset(gfx, self, "front_bumper", state.FrontBumperSeverity, isAlertActive("FrontBumperSeverity", alertClock), alertClock, centerX, y + height * 0.095, height * 0.08);
    drawAsset(gfx, self, "rear_bumper", state.RearBumperSeverity, isAlertActive("RearBumperSeverity", alertClock), alertClock, centerX, y + height * 0.905, height * 0.08);
    drawAsset(gfx, self, "radiator_cooling", state.RadiatorSeverity, isAlertActive("RadiatorSeverity", alertClock), alertClock, centerX, y + height * 0.20, radiatorHeight);
    drawAsset(gfx, self, "engine_gearbox", state.EngineSeverity, isAlertActive("EngineSeverity", alertClock), alertClock, centerX, y + height * 0.405, engineHeight);
    drawAsset(gfx, self, "differential", state.DifferentialSeverity, isAlertActive("DifferentialSeverity", alertClock), alertClock, centerX, y + height * 0.65, transmissionHeight);
    drawAsset(gfx, self, "left_door", state.LeftDoorSeverity, isAlertActive("LeftDoorSeverity", alertClock), alertClock, leftDoorX, doorY, doorHeight);
    drawAsset(gfx, self, "right_door", state.RightDoorSeverity, isAlertActive("RightDoorSeverity", alertClock), alertClock, rightDoorX, doorY, doorHeight);

    drawWheel(gfx, self, state.FrontLeftWheelSeverity, isAlertActive("FrontLeftWheelSeverity", alertClock), alertClock, leftWheelX, frontWheelY, wheelHeight);
    drawWheel(gfx, self, state.FrontRightWheelSeverity, isAlertActive("FrontRightWheelSeverity", alertClock), alertClock, rightWheelX, frontWheelY, wheelHeight);
    drawWheel(gfx, self, state.RearLeftWheelSeverity, isAlertActive("RearLeftWheelSeverity", alertClock), alertClock, leftWheelX, rearWheelY, wheelHeight);
    drawWheel(gfx, self, state.RearRightWheelSeverity, isAlertActive("RearRightWheelSeverity", alertClock), alertClock, rightWheelX, rearWheelY, wheelHeight);

    drawDetailStack(gfx, self, leftDetailX, frontDetailTop, detailHeight, detailGap, state.FrontLeftBrakeSeverity, isAlertActive("FrontLeftBrakeSeverity", alertClock), state.FrontLeftSuspensionSeverity, isAlertActive("FrontLeftSuspensionSeverity", alertClock), state.FrontLeftTyreSeverity, isAlertActive("FrontLeftTyreSeverity", alertClock), alertClock);
    drawDetailStack(gfx, self, rightDetailX, frontDetailTop, detailHeight, detailGap, state.FrontRightBrakeSeverity, isAlertActive("FrontRightBrakeSeverity", alertClock), state.FrontRightSuspensionSeverity, isAlertActive("FrontRightSuspensionSeverity", alertClock), state.FrontRightTyreSeverity, isAlertActive("FrontRightTyreSeverity", alertClock), alertClock);
    drawDetailStack(gfx, self, leftDetailX, rearDetailTop, detailHeight, detailGap, state.RearLeftBrakeSeverity, isAlertActive("RearLeftBrakeSeverity", alertClock), state.RearLeftSuspensionSeverity, isAlertActive("RearLeftSuspensionSeverity", alertClock), state.RearLeftTyreSeverity, isAlertActive("RearLeftTyreSeverity", alertClock), alertClock);
    drawDetailStack(gfx, self, rightDetailX, rearDetailTop, detailHeight, detailGap, state.RearRightBrakeSeverity, isAlertActive("RearRightBrakeSeverity", alertClock), state.RearRightSuspensionSeverity, isAlertActive("RearRightSuspensionSeverity", alertClock), state.RearRightTyreSeverity, isAlertActive("RearRightTyreSeverity", alertClock), alertClock);
end

function onExit()
    local brushes = resources["brushes"];
    for _, brush in pairs(brushes) do
        brush.Dispose();
    end
end
