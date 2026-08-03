-- Read-only adapter around BeamNG's own damageTracker module. The tracker
-- batches change notifications, so the desktop bridge receives damage only
-- when state changes instead of on every telemetry tick.

local M = {}
local callbackRegistered = false

local function sendDamageUpdate(currentDamage, changedDamage)
  obj:queueGameEngineLua(
    "local bridge = extensions.scripts_ztmz__pacenote__rally__bridge_extension; " ..
    "if bridge and bridge.onVehicleDamage then bridge.onVehicleDamage(" ..
    tostring(obj:getId()) .. ", " .. serialize(currentDamage or {}) .. ", " ..
    serialize(changedDamage or {}) .. ") end")
end

local function registerDamageCallback()
  if callbackRegistered or damageTracker == nil or
      type(damageTracker.registerDamageUpdateCallback) ~= "function" then
    return callbackRegistered
  end

  damageTracker.registerDamageUpdateCallback(sendDamageUpdate)
  callbackRegistered = true
  return true
end

function M.onExtensionLoaded()
  callbackRegistered = false
  registerDamageCallback()
end

function M.updateGFX()
  if registerDamageCallback() then
    M.updateGFX = function() end
  end
end

return M
