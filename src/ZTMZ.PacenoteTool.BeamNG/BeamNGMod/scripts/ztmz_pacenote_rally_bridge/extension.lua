-- Read-only bridge for BeamNG.drive's built-in Rally Mode.
-- This core module is registered by modScript.lua, so it starts when BeamNG loads
-- the enabled mod. The desktop application validates the marker and version.

local M = {}

local MAGIC = "ZTMZ_BNG_RALLY"
local PROTOCOL_VERSION = 1
local HOST = "127.0.0.1"
local PORT = 44445
local SEND_INTERVAL_SECONDS = 0.10
local STATUS_INTERVAL_SECONDS = 2.0
local VEHICLE_TELEMETRY_INTERVAL_SECONDS = 0.5

local socketOk, socket = pcall(require, "socket")
local udp = nil
local elapsed = SEND_INTERVAL_SECONDS
local statusElapsed = STATUS_INTERVAL_SECONDS
local vehicleTelemetryElapsed = VEHICLE_TELEMETRY_INTERVAL_SECONDS
local lastExportedStageId = nil
local vehicleTelemetry = {vehicleId = 0, maxRPM = 0, idleRPM = 0}
local damageReporterVehicleId = 0
local rallyRuntime = {activeState = "", countdownNumber = 0}

local function asString(value)
  local valueType = type(value)
  if valueType == "string" then
    return value
  end
  if valueType == "number" or valueType == "boolean" then
    return tostring(value)
  end
  return ""
end

local function asNumber(value)
  if type(value) == "number" then
    return value
  end
  return tonumber(value) or 0
end

local function asBoolean(value)
  if type(value) == "boolean" then
    return value
  end
  if type(value) == "number" then
    return value ~= 0
  end
  return false
end

local function readMember(target, names)
  if target == nil then
    return nil
  end

  for _, name in ipairs(names) do
    local ok, member = pcall(function() return target[name] end)
    if ok and member ~= nil then
      if type(member) == "function" then
        local called, result = pcall(member, target)
        if called and result ~= nil then
          return result
        end
      else
        return member
      end
    end
  end

  return nil
end

local function getRallyManager()
  local rallyExtension = rawget(_G, "gameplay_rally")
  return readMember(rallyExtension, { "getRallyManager" })
end

local function getActiveStageData(manager)
  return readMember(manager, { "getActiveStageData" })
end

local function getPlayerVehicleObject()
  local getPlayerVehicle = rawget(_G, "getPlayerVehicle")
  if type(getPlayerVehicle) ~= "function" then
    return nil
  end

  local ok, vehicle = pcall(getPlayerVehicle, 0)
  return ok and vehicle or nil
end

local function getVehicleId(vehicle)
  return math.floor(asNumber(readMember(vehicle, { "getId", "getID" })))
end

local function updateVehicleTelemetry(vehicleId, fieldName, response)
  if vehicleTelemetry.vehicleId ~= vehicleId then
    return
  end

  local value = asNumber(readMember(response, { "result" }))
  if value <= 0 then
    return
  end

  vehicleTelemetry[fieldName] = value
end

local function ensureDamageReporter(vehicle, vehicleId)
  if damageReporterVehicleId == vehicleId then
    return
  end

  local queued = pcall(function()
    vehicle:queueLuaCommand('extensions.load("ztmz_pacenote_rally_bridge_damage")')
  end)
  if queued then
    damageReporterVehicleId = vehicleId
  end
end

local function requestVehicleTelemetry()
  local vehicle = getPlayerVehicleObject()
  local vehicleId = getVehicleId(vehicle)
  if vehicle == nil or vehicleId <= 0 then
    vehicleTelemetry = {vehicleId = 0, maxRPM = 0, idleRPM = 0}
    damageReporterVehicleId = 0
    return
  end

  if vehicleTelemetry.vehicleId ~= vehicleId then
    vehicleTelemetry = {vehicleId = vehicleId, maxRPM = 0, idleRPM = 0}
  end
  ensureDamageReporter(vehicle, vehicleId)

  local vehicleBridge = rawget(_G, "core_vehicleBridge")
  if type(vehicleBridge) ~= "table" or type(vehicleBridge.requestValue) ~= "function" then
    return
  end

  pcall(vehicleBridge.requestValue, vehicle, function(response)
    updateVehicleTelemetry(vehicleId, "maxRPM", response)
  end, "electricsValue", "maxrpm")
  pcall(vehicleBridge.requestValue, vehicle, function(response)
    updateVehicleTelemetry(vehicleId, "idleRPM", response)
  end, "electricsValue", "idlerpm")
end

local function normalizePacenote(pacenote)
  if pacenote == nil then
    return nil
  end

  local compiled = readMember(pacenote, { "asCompiled" })
  local visualEvent = compiled and readMember(compiled, { "visualPacenoteEvent" }) or nil
  local isFreeform = asBoolean(readMember(pacenote, { "isAudioModeFreeform" }))
  local structured = readMember(pacenote, { "structuredForProjection" })
  if type(structured) ~= "table" or type(readMember(structured, { "items" })) ~= "table" then
    structured = nil
  end

  return {
    id = asString(readMember(pacenote, { "id", "getId" }) or readMember(compiled, { "id" })),
    name = asString(readMember(pacenote, { "name", "getName" }) or readMember(compiled, { "name" })),
    text = asString(readMember(compiled, { "noteText" }) or readMember(pacenote, { "noteOutputFreeform", "name" })),
    sourceText = isFreeform and asString(readMember(pacenote, { "noteOutputFreeform" })) or "",
    sourceLanguage = asString(readMember(pacenote, { "selectedCodriverLanguage" })),
    audioMode = asString(readMember(pacenote, { "audioMode", "getAudioModeString" })),
    structured = structured,
    visual = asString(readMember(visualEvent, { "visualPacenotes" })),
    distanceBeforeMeters = asNumber(readMember(pacenote, { "getNoteFieldBeforeMeters", "distanceBeforeMeters" })),
    distanceAfterMeters = asNumber(readMember(pacenote, { "getNoteFieldAfterMeters", "distanceAfterMeters" })),
    distanceToMeters = 0
  }
end

local function getCurrentPacenote(manager)
  return readMember(manager, { "getLastPlayedPacenote", "getCurrentPacenote", "getNextPacenote" })
end

local function getStagePacenotes(manager)
  local notebook = readMember(manager, { "notebook", "getNotebookPath" })
  local pacenoteCollection = readMember(notebook, { "pacenotes" })
  local pacenotes = readMember(pacenoteCollection, { "sorted" })
  if type(pacenotes) ~= "table" then
    return nil
  end

  local normalized = {}
  for _, pacenote in ipairs(pacenotes) do
    local item = normalizePacenote(pacenote)
    if item ~= nil then
      table.insert(normalized, item)
    end
  end

  return #normalized > 0 and normalized or nil
end

local function buildStage(manager, stageData)
  local completion = stageData and readMember(stageData, { "completion" }) or nil
  local observerState = readMember(manager, { "getStageObserverState" })
  local tracker = readMember(manager, { "vehicleTracker" })
  local drivelineRoute = readMember(manager, { "getDrivelineRoute", "drivelineRoute" })

  return {
    id = asString(readMember(manager, { "getMissionId", "missionId" })),
    name = asString(readMember(manager, { "getMissionName", "missionName" })),
    isActive = asBoolean(readMember(stageData, { "isActive" })),
    isCountingDown = rallyRuntime.activeState == "countdown",
    countdownNumber = rallyRuntime.activeState == "countdown" and rallyRuntime.countdownNumber or 0,
    isFinished = asBoolean(readMember(stageData, { "isComplete" })) or
      asBoolean(readMember(observerState, { "stageComplete" })),
    stageTimeSeconds = asNumber(readMember(stageData, { "currentSSTime" }) or
      readMember(manager, { "getStageRuntimeTime" })),
    distanceMeters = asNumber(readMember(tracker, { "distFromStart" })),
    totalDistanceMeters = asNumber(readMember(completion, { "totalDistM" }) or
      readMember(drivelineRoute, { "getDistanceMeters" })),
    completion = asNumber(readMember(completion, { "distPct" }))
  }
end

local function ensureUdp()
  if not socketOk or socket == nil then
    return false, "LuaSocket is unavailable"
  end

  if udp ~= nil then
    return true, nil
  end

  local client, errorMessage = socket.udp()
  if client == nil then
    return false, errorMessage or "Cannot create UDP socket"
  end

  client:settimeout(0)
  local connected, connectError = client:setpeername(HOST, PORT)
  if not connected then
    client:close()
    return false, connectError or "Cannot configure UDP socket"
  end

  udp = client
  return true, nil
end

local function sendPacket(packet)
  local ready, socketError = ensureUdp()
  if not ready then
    return false, socketError
  end

  packet.magic = MAGIC
  packet.protocolVersion = PROTOCOL_VERSION
  local encoder = rawget(_G, "jsonEncode")
  if type(encoder) ~= "function" then
    return false, "jsonEncode is unavailable"
  end

  local encodedOk, encoded = pcall(encoder, packet)
  if not encodedOk then
    return false, encoded
  end

  local sent, sendError = udp:send(encoded)
  if not sent then
    return false, sendError or "UDP send failed"
  end

  return true, nil
end

local function sendStatus(status, errorMessage)
  sendPacket({
    type = "status",
    status = status,
    error = errorMessage or ""
  })
end

function M.onRallyDataUpdated(data)
  if type(data) ~= "table" then
    return
  end

  rallyRuntime.activeState = asString(data.activeState)
  local countdownData = data.countdownData
  rallyRuntime.countdownNumber = type(countdownData) == "table" and
    math.floor(asNumber(countdownData.countdown)) or 0
end

function M.onExtensionLoaded()
  elapsed = SEND_INTERVAL_SECONDS
  statusElapsed = STATUS_INTERVAL_SECONDS
  vehicleTelemetryElapsed = VEHICLE_TELEMETRY_INTERVAL_SECONDS
  lastExportedStageId = nil
  vehicleTelemetry = {vehicleId = 0, maxRPM = 0, idleRPM = 0}
  damageReporterVehicleId = 0
  rallyRuntime = {activeState = "", countdownNumber = 0}
  sendStatus("started", "")
end

function M.onExtensionUnloaded()
  if udp ~= nil then
    udp:close()
    udp = nil
  end
end

function M.onVehicleDamage(vehicleId, currentDamage, changedDamage)
  vehicleId = math.floor(asNumber(vehicleId))
  if vehicleId <= 0 or vehicleId ~= vehicleTelemetry.vehicleId then
    return
  end

  if type(currentDamage) ~= "table" or type(changedDamage) ~= "table" then
    return
  end

  sendPacket({
    type = "damage",
    status = "active",
    telemetry = vehicleTelemetry,
    damage = {
      vehicleId = vehicleId,
      current = currentDamage,
      changed = changedDamage
    }
  })
end

function M.onUpdate(dtReal)
  elapsed = elapsed + (dtReal or 0)
  statusElapsed = statusElapsed + (dtReal or 0)
  vehicleTelemetryElapsed = vehicleTelemetryElapsed + (dtReal or 0)
  if vehicleTelemetryElapsed >= VEHICLE_TELEMETRY_INTERVAL_SECONDS then
    vehicleTelemetryElapsed = 0
    requestVehicleTelemetry()
  end

  if elapsed < SEND_INTERVAL_SECONDS then
    return
  end
  elapsed = 0

  local manager = getRallyManager()
  if manager == nil then
    lastExportedStageId = nil
    if statusElapsed >= STATUS_INTERVAL_SECONDS then
      statusElapsed = 0
      sendStatus("waiting", "Rally manager is unavailable")
    end
    return
  end

  local stageData = getActiveStageData(manager)
  local stage = buildStage(manager, stageData)
  if stage.id == "" then
    if statusElapsed >= STATUS_INTERVAL_SECONDS then
      statusElapsed = 0
      sendStatus("waiting", "Rally stage is unavailable")
    end
    return
  end

  sendPacket({
    type = "stage",
    status = "active",
    telemetry = vehicleTelemetry,
    stage = stage,
    pacenote = normalizePacenote(getCurrentPacenote(manager))
  })

  if stage.id ~= lastExportedStageId then
    local pacenotes = getStagePacenotes(manager)
    if pacenotes ~= nil then
      sendPacket({
        type = "stagePacenotes",
        status = "active",
        stage = stage,
        pacenotes = pacenotes
      })
      lastExportedStageId = stage.id
    end
  end
end

setExtensionUnloadMode(M, "manual")

return M
