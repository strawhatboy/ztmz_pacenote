local resources = {};
local frames = 0;
local whRatio = 1.22;
local aspectRatios = {
    brake = 1.06,
    differential = 2.39,
    engine_gearbox = 1.60,
    front_bumper = 2.27,
    left_door = 1.22,
    radiator_cooling = 1.54,
    rear_bumper = 2.26,
    right_door = 1.20,
    suspension = 0.36,
    tyre = 0.97,
    wheel = 0.99
};

function onInit(args)
    local gfx = args.Graphics;
    local brushes = {};
    brushes["background"] = gfx.CreateSolidBrush(10, 16, 20, 182);
    brushes["outline"] = gfx.CreateSolidBrush(104, 123, 132, 180);
    brushes["warning"] = gfx.CreateSolidBrush(246, 188, 68, 88);
    brushes["critical"] = gfx.CreateSolidBrush(239, 82, 71, 124);
    brushes["criticalMuted"] = gfx.CreateSolidBrush(145, 63, 58, 42);
    resources["brushes"] = brushes;
end

function getStateBrush(severity)
    local brushes = resources["brushes"];
    if severity <= 0 then
        return nil;
    end
    if severity >= 4 then
        return math.floor(frames / 12) % 2 == 0 and brushes["critical"] or brushes["criticalMuted"];
    end
    if severity >= 3 then
        return brushes["critical"];
    end
    if severity >= 1 then
        return brushes["warning"];
    end
    return brushes["warning"];
end

function drawAsset(gfx, self, key, severity, centerX, centerY, height)
    local image = self.ImageResources["images@" .. key];
    local width = height * (aspectRatios[key] or 1);
    local x = centerX - width * 0.5;
    local y = centerY - height * 0.5;
    local stateBrush = getStateBrush(severity);
    if stateBrush ~= nil then
        local padding = math.max(2, height * 0.10);
        gfx.FillRoundedRectangle(stateBrush, x - padding, y - padding, x + width + padding, y + height + padding, math.max(2, height * 0.13));
    end
    if image ~= nil then
        gfx.DrawImage(image, x, y, x + width, y + height);
    end
end

function drawWheel(gfx, self, severity, centerX, centerY, height)
    drawAsset(gfx, self, "wheel", severity, centerX, centerY, height);
end

function drawDetailStack(gfx, self, centerX, top, detailHeight, detailGap, brakeSeverity, suspensionSeverity, tyreSeverity)
    drawAsset(gfx, self, "brake", brakeSeverity, centerX, top + detailHeight * 0.5, detailHeight);
    drawAsset(gfx, self, "suspension", suspensionSeverity, centerX, top + detailHeight * 1.5 + detailGap, detailHeight);
    drawAsset(gfx, self, "tyre", tyreSeverity, centerX, top + detailHeight * 2.5 + detailGap * 2, detailHeight);
end

function onUpdate(args)
    frames = frames + 1;
    local data = args.GameData;
    local damage = data.CarDamage;
    if damage == nil or damage.Dashboard == nil or not damage.Dashboard.IsAvailable then
        return;
    end

    local gfx = args.Graphics;
    local self = args.Self;
    local state = damage.Dashboard;
    local brushes = resources["brushes"];
    local x, y, width, height = getDashboardPositionStart(self, gfx, whRatio);
    local centerX = x + width * 0.5;
    local centerY = y + height * 0.5;
    local bodyWidth = height * 0.76;
    local bodyLeft = centerX - bodyWidth * 0.5;
    local bodyRight = centerX + bodyWidth * 0.5;
    local bodyTop = y + height * 0.10;
    local bodyBottom = y + height * 0.90;
    local centralIconSize = height * 0.18;
    local doorHeight = height * 0.17;
    local doorWidth = doorHeight * aspectRatios["left_door"];
    local wheelHeight = height * 0.18;
    local wheelWidth = wheelHeight * aspectRatios["wheel"];
    local frontWheelY = y + height * 0.28;
    local rearWheelY = y + height * 0.72;
    local detailHeight = height * 0.08;
    local detailGap = height * 0.016;
    local doorTop = centerY - doorHeight * 0.5;
    local doorBottom = centerY + doorHeight * 0.5;
    local frontDetailTop = bodyTop + height * 0.02;
    local rearDetailTop = doorBottom + height * 0.02;
    local leftDetailX = bodyLeft + detailHeight * 0.75;
    local rightDetailX = bodyRight - detailHeight * 0.75;

    gfx.FillRoundedRectangle(brushes["background"], x, y, x + width, y + height, height * 0.05);

    -- A minimal top-down chassis keeps the component layout legible without becoming a card grid.
    gfx.DrawRectangle(brushes["outline"], bodyLeft, bodyTop, bodyRight, bodyBottom, height * 0.008);
    gfx.DrawLine(brushes["outline"], centerX, bodyTop + height * 0.08, centerX, bodyBottom - height * 0.08, height * 0.006);

    drawAsset(gfx, self, "front_bumper", state.FrontBumperSeverity, centerX, y + height * 0.08, height * 0.11);
    drawAsset(gfx, self, "rear_bumper", state.RearBumperSeverity, centerX, y + height * 0.92, height * 0.11);
    drawAsset(gfx, self, "radiator_cooling", state.RadiatorSeverity, centerX, y + height * 0.27, centralIconSize);
    drawAsset(gfx, self, "engine_gearbox", state.EngineSeverity, centerX, y + height * 0.48, centralIconSize);
    drawAsset(gfx, self, "differential", state.DifferentialSeverity, centerX, y + height * 0.69, centralIconSize * 0.80);
    drawAsset(gfx, self, "left_door", state.LeftDoorSeverity, bodyLeft + doorWidth * 0.5, centerY, doorHeight);
    drawAsset(gfx, self, "right_door", state.RightDoorSeverity, bodyRight - doorWidth * 0.5, centerY, doorHeight);

    drawWheel(gfx, self, state.FrontLeftWheelSeverity, bodyLeft - wheelWidth * 0.5, frontWheelY, wheelHeight);
    drawWheel(gfx, self, state.FrontRightWheelSeverity, bodyRight + wheelWidth * 0.5, frontWheelY, wheelHeight);
    drawWheel(gfx, self, state.RearLeftWheelSeverity, bodyLeft - wheelWidth * 0.5, rearWheelY, wheelHeight);
    drawWheel(gfx, self, state.RearRightWheelSeverity, bodyRight + wheelWidth * 0.5, rearWheelY, wheelHeight);

    drawDetailStack(gfx, self, leftDetailX, frontDetailTop, detailHeight, detailGap, state.FrontLeftBrakeSeverity, state.FrontLeftSuspensionSeverity, state.FrontLeftTyreSeverity);
    drawDetailStack(gfx, self, rightDetailX, frontDetailTop, detailHeight, detailGap, state.FrontRightBrakeSeverity, state.FrontRightSuspensionSeverity, state.FrontRightTyreSeverity);
    drawDetailStack(gfx, self, leftDetailX, rearDetailTop, detailHeight, detailGap, state.RearLeftBrakeSeverity, state.RearLeftSuspensionSeverity, state.RearLeftTyreSeverity);
    drawDetailStack(gfx, self, rightDetailX, rearDetailTop, detailHeight, detailGap, state.RearRightBrakeSeverity, state.RearRightSuspensionSeverity, state.RearRightTyreSeverity);
end

function onExit()
    local brushes = resources["brushes"];
    for _, brush in pairs(brushes) do
        brush.Dispose();
    end
end
