-- Read-only adapter around BeamNG's own damageTracker module. The tracker
-- batches change notifications, so the desktop bridge receives damage only
-- when state changes instead of on every telemetry tick.

local M = {}
local callbackRegistered = false
local initialSnapshotPending = true

local function sendDamageUpdate(currentDamage, changedDamage)
  -- The first callback establishes the vehicle's current health. It must not
  -- look like a new damage event to the desktop tool.
  local forwardedChanges = initialSnapshotPending and {} or (changedDamage or {})
  initialSnapshotPending = false

  obj:queueGameEngineLua(
    "local bridge = extensions.scripts_ztmz__pacenote__rally__bridge_extension; " ..
    "if bridge and bridge.onVehicleDamage then bridge.onVehicleDamage(" ..
    tostring(obj:getId()) .. ", " .. serialize(currentDamage or {}) .. ", " ..
    serialize(forwardedChanges) .. ") end")
end

local function registerDamageCallback()
  if callbackRegistered or damageTracker == nil or
      type(damageTracker.registerDamageUpdateCallback) ~= "function" then
    return callbackRegistered
  end

  damageTracker.registerDamageUpdateCallback(sendDamageUpdate)
  callbackRegistered = true
  if type(damageTracker.sendNow) == "function" then
    damageTracker.sendNow()
  end
  return true
end

function M.onExtensionLoaded()
  callbackRegistered = false
  initialSnapshotPending = true
  registerDamageCallback()
end

function M.updateGFX()
  if registerDamageCallback() then
    M.updateGFX = function() end
  end
end

return M
