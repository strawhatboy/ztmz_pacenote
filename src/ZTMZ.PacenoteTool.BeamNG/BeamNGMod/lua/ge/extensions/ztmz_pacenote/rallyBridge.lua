-- Read-only bridge for BeamNG.drive Rally Mode.
-- The desktop application validates the magic value and protocol version before use.

local M = {}

local MAGIC = "ZTMZ_BNG_RALLY"
local PROTOCOL_VERSION = 1
local HOST = "127.0.0.1"
local PORT = 44445
local SEND_INTERVAL_SECONDS = 0.10
local STATUS_INTERVAL_SECONDS = 2.0

local socketOk, socket = pcall(require, "socket")
local udp = nil
local enabled = false
local elapsed = 0
local statusElapsed = STATUS_INTERVAL_SECONDS
local lastExportedStageId = nil

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

-- Rally Mode internals are experimental. Read fields and methods defensively so a
-- BeamNG update reports unavailable data instead of raising an engine Lua error.
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
  if rallyExtension == nil then
    return nil
  end

  local manager = readMember(rallyExtension, { "getRallyManager", "rallyManager" })
  return manager
end

local function getStage(manager)
  return readMember(manager, {
    "getCurrentStage",
    "getActiveStage",
    "getStage",
    "currentStage",
    "activeStage",
    "stage"
  })
end

local function normalizePacenote(pacenote)
  if pacenote == nil then
    return nil
  end

  return {
    id = asString(readMember(pacenote, { "id", "getId", "pacenoteId" })),
    name = asString(readMember(pacenote, { "name", "getName", "label" })),
    text = asString(readMember(pacenote, { "renderedText", "text", "getText", "displayText", "getDisplayText" })),
    visual = asString(readMember(pacenote, { "visual", "visualData", "getVisual", "symbol" })),
    distanceBeforeMeters = asNumber(readMember(pacenote, {
      "distanceBeforeMeters", "beforeDistance", "distanceCallBefore", "getDistanceBefore"
    })),
    distanceAfterMeters = asNumber(readMember(pacenote, {
      "distanceAfterMeters", "afterDistance", "distanceCallAfter", "getDistanceAfter"
    })),
    distanceToMeters = asNumber(readMember(pacenote, {
      "distanceToMeters", "distance", "distanceTo", "getDistance"
    }))
  }
end

local function getCurrentPacenote(manager, stage)
  return readMember(stage, {
    "getNextPacenote",
    "getCurrentPacenote",
    "getActivePacenote",
    "nextPacenote",
    "currentPacenote"
  }) or readMember(manager, {
    "getNextPacenote",
    "getCurrentPacenote",
    "getActivePacenote",
    "nextPacenote",
    "currentPacenote"
  })
end

local function getStagePacenotes(manager, stage)
  local pacenotes = readMember(stage, {
    "getCompiledPacenotes",
    "getPacenotes",
    "getPacenoteList",
    "compiledPacenotes",
    "pacenotes"
  }) or readMember(manager, {
    "getCompiledPacenotes",
    "getPacenotes",
    "getPacenoteList",
    "compiledPacenotes",
    "pacenotes"
  })

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

local function buildStage(manager, stage)
  local source = stage or manager
  return {
    id = asString(readMember(source, { "id", "stageId", "missionId", "getId", "getStageId" })),
    name = asString(readMember(source, { "name", "stageName", "getName", "getStageName" })),
    isActive = asBoolean(readMember(manager, {
      "isStageActive", "getIsStageActive", "isActive", "active"
    })),
    isCountingDown = asBoolean(readMember(manager, {
      "isCountingDown", "getIsCountingDown", "countingDown", "isStaged"
    })),
    isFinished = asBoolean(readMember(manager, {
      "isStageFinished", "getIsStageFinished", "finished", "isFinished"
    })),
    stageTimeSeconds = asNumber(readMember(manager, {
      "getStageTime", "getCurrentStageTime", "getElapsedTime", "stageTime", "elapsedTime"
    })),
    distanceMeters = asNumber(readMember(manager, {
      "getDistanceCompleted", "getStageDistance", "distanceCompleted", "distanceMeters"
    })),
    totalDistanceMeters = asNumber(readMember(manager, {
      "getTotalDistance", "getStageLength", "totalDistance", "totalDistanceMeters"
    })),
    completion = asNumber(readMember(manager, {
      "getCompletion", "getCompletionRate", "completion", "progress"
    }))
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

function M.start()
  enabled = true
  elapsed = SEND_INTERVAL_SECONDS
  statusElapsed = STATUS_INTERVAL_SECONDS
  lastExportedStageId = nil
  sendStatus("started", "")
end

function M.stop()
  if enabled then
    sendStatus("stopped", "")
  end
  enabled = false
  if udp ~= nil then
    udp:close()
    udp = nil
  end
end

function M.toggle()
  if enabled then
    M.stop()
  else
    M.start()
  end
end

function M.onExtensionUnloaded()
  M.stop()
end

function M.onUpdate(dtReal)
  if not enabled then
    return
  end

  elapsed = elapsed + (dtReal or 0)
  statusElapsed = statusElapsed + (dtReal or 0)
  if elapsed < SEND_INTERVAL_SECONDS then
    return
  end
  elapsed = 0

  local manager = getRallyManager()
  if manager == nil then
    if statusElapsed >= STATUS_INTERVAL_SECONDS then
      statusElapsed = 0
      sendStatus("waiting", "Rally manager is unavailable")
    end
    return
  end

  local stage = getStage(manager)
  local stageData = buildStage(manager, stage)
  local currentPacenote = normalizePacenote(getCurrentPacenote(manager, stage))
  sendPacket({
    type = "stage",
    status = "active",
    stage = stageData,
    pacenote = currentPacenote
  })

  if stageData.id ~= "" and stageData.id ~= lastExportedStageId then
    local pacenotes = getStagePacenotes(manager, stage)
    if pacenotes ~= nil then
      sendPacket({
        type = "stagePacenotes",
        status = "active",
        stage = stageData,
        pacenotes = pacenotes
      })
      lastExportedStageId = stageData.id
    end
  end
end

return M
