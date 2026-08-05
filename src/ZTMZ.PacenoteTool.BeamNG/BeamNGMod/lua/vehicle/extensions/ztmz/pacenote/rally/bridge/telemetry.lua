-- Vehicle-side values that BeamNG's standard OutGauge protocol does not carry.
-- The game-engine bridge sends the latest snapshot with its regular UDP packet.

local M = {}
local REPORT_INTERVAL_SECONDS = 1 / 60
local reportElapsed = REPORT_INTERVAL_SECONDS

local function finiteNumber(value)
  return type(value) == "number" and value == value and value ~= math.huge and value ~= -math.huge
end

local function numberOrZero(value)
  return finiteNumber(value) and value or 0
end

local function clamp(value, minimum, maximum)
  return math.max(minimum, math.min(maximum, value))
end

local function reportTelemetry()
  local gravity = math.abs(numberOrZero(obj:getGravity()))
  if gravity < 0.001 then
    gravity = 9.81
  end

  local electricsValues = electrics and electrics.values or {}
  local sensorValues = sensors or {}
  local steeringInput = electricsValues.steering_input
  if not finiteNumber(steeringInput) then
    steeringInput = electricsValues.steering
  end

  local telemetry = {
    maxRPM = numberOrZero(electricsValues.maxrpm),
    idleRPM = numberOrZero(electricsValues.idlerpm),
    -- Use the player's normalized input rather than the assisted steering value.
    -- BeamNG's live sign already matches the dashboard convention used by the other games.
    steering = clamp(numberOrZero(steeringInput), -1, 1),
    handBrake = clamp(numberOrZero(electricsValues.parkingbrake), 0, 1),
    maxGears = math.max(0, numberOrZero(electricsValues.maxGearIndex)),
    -- BeamNG vehicle sensors are metres per second squared: X is lateral and
    -- Y is longitudinal. Use the smoothed readings for a stable dashboard ball.
    gLong = numberOrZero(sensorValues.gy2) / gravity,
    gLat = -numberOrZero(sensorValues.gx2) / gravity
  }

  obj:queueGameEngineLua(
    "local bridge = extensions.scripts_ztmz__pacenote__rally__bridge_extension; " ..
    "if bridge and bridge.onVehicleTelemetry then bridge.onVehicleTelemetry(" ..
    tostring(obj:getId()) .. ", " .. serialize(telemetry) .. ") end")
end

function M.onExtensionLoaded()
  reportElapsed = REPORT_INTERVAL_SECONDS
end

function M.updateGFX(dt)
  reportElapsed = reportElapsed + (dt or 0)
  if reportElapsed < REPORT_INTERVAL_SECONDS then
    return
  end

  reportElapsed = reportElapsed - REPORT_INTERVAL_SECONDS
  reportTelemetry()
end

return M
