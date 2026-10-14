using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRMemDataReader
{
    private const int DamageTelemetryBlockPointerOffset = 0x620;
    private const int DamageTelemetryBlockLength = 0x48C;
    private static readonly TimeSpan DamageTelemetrySampleInterval = TimeSpan.FromMilliseconds(200);

    private readonly object _syncRoot = new();
    private readonly RBRDamageTelemetryProbe _damageTelemetryProbe = new();
    private bool _isProcessOpened = false;
    private Process? _theProcess;
    private RBRNativeResultReader? _nativeResultReader;
    private RBRLoadedPacenoteReader? _loadedPacenoteReader;
    private IntPtr _processHandle = IntPtr.Zero;
    private DateTimeOffset _nextDamageTelemetrySampleUtc = DateTimeOffset.MinValue;

    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public bool OpenProcess(IGame game)
    {
        lock (_syncRoot)
        {
            if (_isProcessOpened)
            {
                return true;
            }

            var rbrProcesses = Process.GetProcessesByName(game.Executable);
            if (rbrProcesses.Length == 0)
            {
                _logger.Error("RBR process not found.");
                return false;
            }

            var rbrProcess = rbrProcesses[0];
            try
            {
                rbrProcess.Refresh();
                if (rbrProcess.HasExited)
                {
                    _logger.Error("RBR process has exited.");
                    return false;
                }

                var processHandle = MemoryReader.OpenProcess(16, false, rbrProcess.Id);
                if (processHandle == IntPtr.Zero)
                {
                    _logger.Error("Failed to open RBR process.");
                    return false;
                }

                _theProcess = rbrProcess;
                _processHandle = processHandle;
                _isProcessOpened = true;
                _nativeResultReader = null;
                try
                {
                    _nativeResultReader = RBRNativeResultReader.Create(rbrProcess, processHandle);
                    _logger.Info("RBR native retirement result reader enabled: {0}", _nativeResultReader != null);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "RBR native result layout could not be verified; retirement reason remains unknown.");
                }
                _logger.Info("RBR process opened.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to open RBR process.");
                return false;
            }
            finally
            {
                if (!_isProcessOpened || !ReferenceEquals(_theProcess, rbrProcess))
                {
                    rbrProcess.Dispose();
                }
            }
        }
    }

    public RBRMemData ReadMemData(IGame game)
    {
        lock (_syncRoot)
        {
            if (!OpenProcess(game))
            {
                return default(RBRMemData);
            }

            var pHandle = _processHandle;
            var memData = new RBRMemData();
            memData.IsRunning = true;
            memData.IsRendering = MemoryReader.Read<byte>(pHandle, 0x7C0BBC) > 0;
            memData.GameStateId = MemoryReader.Read<byte>(pHandle, 0x7EAC48, 0x728);
            // Poll menus as well as racing: failure results arrive after the raw end state.
            memData.NativeResult = _nativeResultReader?.Read();

            var rbrGameState = (RBRGameState)memData.GameStateId;

            if (rbrGameState == RBRGameState.RaceBegin || rbrGameState == RBRGameState.Racing || rbrGameState == RBRGameState.ReplayBegin || rbrGameState == RBRGameState.Replay)
            {
                var baseAddr0 = MemoryReader.Read<int>(pHandle, 0x165FC68);
                // racedata available
                memData.SpeedKMH = MemoryReader.Read<float>(pHandle, baseAddr0 + 12);
                memData.RaceTime = MemoryReader.Read<float>(pHandle, baseAddr0 + 0x140);
                memData.DistanceFromStart = MemoryReader.Read<float>(pHandle, baseAddr0 + 32);
                memData.TravelledDistance = MemoryReader.Read<float>(pHandle, baseAddr0 + 36);
                memData.DistanceToFinish = MemoryReader.Read<float>(pHandle, baseAddr0 + 40);
                memData.EngineRPM = MemoryReader.Read<float>(pHandle, baseAddr0 + 16);
                memData.WaterTemperatureCelsius = MemoryReader.Read<float>(pHandle, baseAddr0 + 20);
                memData.TurboPressureBar = MemoryReader.Read<float>(pHandle, baseAddr0 + 24) / 100000f;
                memData.CurrentStagePosition = MemoryReader.Read<float>(pHandle, baseAddr0 + 0x13C);
                memData.WrongWay = MemoryReader.Read<byte>(pHandle, baseAddr0 + 0x150) > 0;
                memData.SessionTime = MemoryReader.Read<float>(pHandle, baseAddr0 + 0x15C);
                memData.GearId = MemoryReader.Read<byte>(pHandle, baseAddr0 + 0x170) - 1;   // -1 = R, 0 = N, 1 = 1 ...
                memData.Split1Time = MemoryReader.Read<int>(pHandle, baseAddr0 + 0x258);
                memData.Split2Time = MemoryReader.Read<int>(pHandle, baseAddr0 + 0x25C);
                memData.Split1Done = memData.Split1Time > 0;
                memData.Split2Done = memData.Split2Time > 0;
                memData.StageStartCountdown = MemoryReader.Read<float>(pHandle, baseAddr0 + 0x244);
                memData.FalseStart = MemoryReader.Read<int>(pHandle, baseAddr0 + 0x248) > 0;
                memData.RaceEnded = MemoryReader.Read<int>(pHandle, baseAddr0 + 0x2C4) == 1;

                var baseAddr1 = MemoryReader.Read<int>(pHandle, 0x8EF660);
                memData.X = MemoryReader.Read<float>(pHandle, baseAddr1 + 0x140);
                memData.Y = MemoryReader.Read<float>(pHandle, baseAddr1 + 0x144);
                memData.Z = MemoryReader.Read<float>(pHandle, baseAddr1 + 0x148);
                memData.XSpin = MemoryReader.Read<float>(pHandle, baseAddr1 + 0x190);
                memData.YSpin = MemoryReader.Read<float>(pHandle, baseAddr1 + 0x194);
                memData.ZSpin = MemoryReader.Read<float>(pHandle, baseAddr1 + 0x198);
                memData.XSpeed = MemoryReader.Read<float>(pHandle, baseAddr1 + 0x1C0);
                memData.YSpeed = MemoryReader.Read<float>(pHandle, baseAddr1 + 0x1C4);
                memData.ZSpeed = MemoryReader.Read<float>(pHandle, baseAddr1 + 0x1C8);

                CaptureDamageTelemetryIfDue(pHandle, baseAddr1);
                memData.DamageTelemetry = _damageTelemetryProbe.CaptureDiagnostics().LatestSnapshot;

                var baseAddr2 = MemoryReader.Read<int>(pHandle, 0x7EAC48);
                memData.Steering = MemoryReader.Read<float>(pHandle, baseAddr2 + 0x794);
                memData.Throttle = MemoryReader.Read<float>(pHandle, baseAddr2 + 0x798);
                memData.Brake = MemoryReader.Read<float>(pHandle, baseAddr2 + 0x79C);
                memData.Handbrake = MemoryReader.Read<float>(pHandle, baseAddr2 + 0x7A0);
                memData.Clutch = MemoryReader.Read<float>(pHandle, baseAddr2 + 0x7A4);

                memData.CarModelId = MemoryReader.Read<int>(pHandle, 0x1660808);
                memData.DamageId = MemoryReader.Read<int>(pHandle, 0x1660850);
                memData.TransmissionId = MemoryReader.Read<int>(pHandle, 0x1660814);
                memData.WeatherId = MemoryReader.Read<int>(pHandle, 0x1660848);
                memData.Track = GetTrackNameFromMemory();
                memData.TrackId = MemoryReader.Read<int>(pHandle, 0x7EA678, 0x70, 0x20);
                // memData.TrackId = MemoryReader.Read<int>(pHandle, 0x8938F8, 0x08);
                // memData.TrackLength = MemoryReader.Read<float>(pHandle, 0x1659184, 0x75310);
                memData.TrackLength = memData.DistanceToFinish + memData.DistanceFromStart;
            }
            else
            {
                _damageTelemetryProbe.SetWaitingForRace();
                _nextDamageTelemetrySampleUtc = DateTimeOffset.MinValue;
            }

            return memData;
        }
    }

    public RBRDamageTelemetryDiagnostics CaptureDamageTelemetryDiagnostics()
    {
        lock (_syncRoot)
        {
            return _damageTelemetryProbe.CaptureDiagnostics();
        }
    }

    private void CaptureDamageTelemetryIfDue(IntPtr processHandle, int physicsAddress)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextDamageTelemetrySampleUtc)
        {
            return;
        }

        _nextDamageTelemetrySampleUtc = now + DamageTelemetrySampleInterval;
        if (!IsPlausiblePointer(physicsAddress))
        {
            ReportDamageTelemetryUnavailable("RBR physics pointer is unavailable.");
            return;
        }

        if (!TryReadInt32(processHandle, AddOffset(physicsAddress, DamageTelemetryBlockPointerOffset), out var damageAddress) ||
            !IsPlausiblePointer(damageAddress))
        {
            ReportDamageTelemetryUnavailable("RBR damage block pointer could not be read.");
            return;
        }

        var damageBlock = new byte[DamageTelemetryBlockLength];
        if (!TryReadBytes(processHandle, damageAddress, damageBlock))
        {
            ReportDamageTelemetryUnavailable("RBR damage block could not be read.");
            return;
        }

        var parts = RBRDamageTelemetryCatalog.PartDefinitions
            .Select(definition => ReadDamagePart(definition, damageAddress, damageBlock))
            .ToArray();
        var snapshot = new RBRDamageTelemetrySnapshot(
            now,
            physicsAddress,
            damageAddress,
            parts,
            RBRDamageTelemetryCatalog.CreateGroups(parts));
        var logEntry = _damageTelemetryProbe.Record(snapshot);
        if (logEntry != null)
        {
            _logger.Info(
                "RBR damage telemetry {0}; changed={1}; {2}",
                logEntry.Kind,
                string.Join(",", logEntry.ChangedComponents),
                logEntry.Snapshot.ToLogText());
        }
    }

    private void ReportDamageTelemetryUnavailable(string error)
    {
        if (_damageTelemetryProbe.ReportUnavailable(error))
        {
            _logger.Warn("RBR damage telemetry unavailable: {0}", error);
        }
    }

    private static RBRDamagePartTelemetry ReadDamagePart(
        RBRDamagePartDefinition definition,
        int damageAddress,
        byte[] damageBlock)
    {
        var address = AddOffset(damageAddress, definition.Offset);
        var byteCount = GetByteCount(definition);
        if (definition.Offset < 0 || byteCount <= 0 || definition.Offset > damageBlock.Length - byteCount)
        {
            return RBRDamageTelemetryCatalog.CreatePart(definition, address, false);
        }

        return definition.ReadKind switch
        {
            RBRDamageReadKind.Float => RBRDamageTelemetryCatalog.CreatePart(
                definition,
                address,
                true,
                BitConverter.ToSingle(damageBlock, definition.Offset)),
            RBRDamageReadKind.Integer or RBRDamageReadKind.Pointer => RBRDamageTelemetryCatalog.CreatePart(
                definition,
                address,
                true,
                integerValue: BitConverter.ToInt32(damageBlock, definition.Offset)),
            RBRDamageReadKind.FloatAndInteger => RBRDamageTelemetryCatalog.CreatePart(
                definition,
                address,
                true,
                BitConverter.ToSingle(damageBlock, definition.Offset),
                BitConverter.ToInt32(damageBlock, definition.Offset + sizeof(float))),
            RBRDamageReadKind.FloatArray => RBRDamageTelemetryCatalog.CreatePart(
                definition,
                address,
                true,
                floatValues: ReadFloatValues(damageBlock, definition.Offset, definition.ValueCount)),
            RBRDamageReadKind.IntegerArray => RBRDamageTelemetryCatalog.CreatePart(
                definition,
                address,
                true,
                integerValues: ReadIntegerValues(damageBlock, definition.Offset, definition.ValueCount)),
            _ => RBRDamageTelemetryCatalog.CreatePart(definition, address, false)
        };
    }

    private static int GetByteCount(RBRDamagePartDefinition definition)
    {
        return definition.ReadKind switch
        {
            RBRDamageReadKind.Float or RBRDamageReadKind.Integer or RBRDamageReadKind.Pointer => sizeof(int),
            RBRDamageReadKind.FloatAndInteger => sizeof(float) + sizeof(int),
            RBRDamageReadKind.FloatArray or RBRDamageReadKind.IntegerArray => sizeof(int) * definition.ValueCount,
            _ => 0
        };
    }

    private static float[] ReadFloatValues(byte[] damageBlock, int offset, int count)
    {
        var values = new float[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = BitConverter.ToSingle(damageBlock, offset + index * sizeof(float));
        }

        return values;
    }

    private static int[] ReadIntegerValues(byte[] damageBlock, int offset, int count)
    {
        var values = new int[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = BitConverter.ToInt32(damageBlock, offset + index * sizeof(int));
        }

        return values;
    }

    private static bool TryReadInt32(IntPtr processHandle, int address, out int value)
    {
        var buffer = new byte[sizeof(int)];
        if (!TryReadBytes(processHandle, address, buffer))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToInt32(buffer, 0);
        return true;
    }

    private static bool TryReadBytes(IntPtr processHandle, int address, byte[] buffer)
    {
        var bytesRead = 0;
        return MemoryReader.ReadProcessMemory((int)processHandle, address, buffer, buffer.Length, ref bytesRead) &&
               bytesRead == buffer.Length;
    }

    private static int AddOffset(int address, int offset)
    {
        return unchecked(address + offset);
    }

    private static bool IsPlausiblePointer(int address)
    {
        return unchecked((uint)address) >= 0x10000;
    }

    public string GetTrackNameFromMemory()
    {
        lock (_syncRoot)
        {
            if (!_isProcessOpened)
            {
                return "";
            }
            var pHandle = _processHandle;
            int trackNameAddr = 0x007D1D64; //right here
            if (trackNameAddr != 0)
            {
                int lpNumberOfBytesRead = 0;
                byte[] array = new byte[2];
                StringBuilder stringBuilder = new StringBuilder();
                int wordCount = 0;
                while (wordCount < 0x40)
                {
                    MemoryReader.ReadProcessMemory((int)pHandle, trackNameAddr, array, array.Length, ref lpNumberOfBytesRead);
                    lpNumberOfBytesRead = BitConverter.ToUInt16(array, 0);
                    if (lpNumberOfBytesRead == 0)
                    {
                        break;
                    }
                    stringBuilder.Append((char)lpNumberOfBytesRead);
                    wordCount += 1;
                    trackNameAddr += 2;
                }
                return stringBuilder.ToString();
            }
            return "";
        }
    }

    internal RBRLoadedPacenote? ReadLoadedPacenote(int trackId)
    {
        lock (_syncRoot)
        {
            if (!_isProcessOpened || _theProcess == null) return null;
            try
            {
                // Discover lazily: RSF may not yet be loaded when the process is first detected.
                if (_loadedPacenoteReader == null)
                {
                    _theProcess.Refresh();
                    _loadedPacenoteReader = RBRLoadedPacenoteReader.Create(_theProcess, _processHandle);
                }
                return _loadedPacenoteReader?.Read(trackId);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to read RBR's loaded pacenote title; using disk selection evidence.");
                return null;
            }
        }
    }

    public void CloseProgress()
    {
        lock (_syncRoot)
        {
            var process = _theProcess;
            var processHandle = _processHandle;

            _isProcessOpened = false;
            _theProcess = null;
            _nativeResultReader = null;
            _loadedPacenoteReader = null;
            _processHandle = IntPtr.Zero;
            _damageTelemetryProbe.SetStopped();
            _nextDamageTelemetrySampleUtc = DateTimeOffset.MinValue;

            try
            {
                if (processHandle != IntPtr.Zero && !MemoryReader.CloseHandle(processHandle))
                {
                    _logger.Warn("Failed to close the RBR memory process handle.");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to close the RBR memory process handle.");
            }
            finally
            {
                process?.Dispose();
            }
        }
    }

    public ScriptReader ReadPacenotesFromMemory()
    {
        lock (_syncRoot)
        {
            if (!_isProcessOpened)
            {
                return null;
            }
            var pHandle = _processHandle;
            var baseAddr = MemoryReader.Read<int>(pHandle, 0x7EABA8, 0x10);
            var pacenoteCount = MemoryReader.Read<int>(pHandle, baseAddr + 0x20);
            var pacenotesAddr = MemoryReader.Read<int>(pHandle, baseAddr + 0x24);
            var records = new List<DynamicPacenoteRecord>();

            if (pacenotesAddr != 0)
            {
                for (int i = 0; i < pacenoteCount; i++)
                {
                    var pacenoteType = MemoryReader.Read<int>(pHandle, pacenotesAddr + 0x0C * i);
                    var pacenoteModifier = MemoryReader.Read<int>(pHandle, pacenotesAddr + 0x4 + 0x0C * i);
                    var pacenoteDistance = MemoryReader.Read<float>(pHandle, pacenotesAddr + 0x8 + 0x0C * i);

                    var record = RBRGamePacenoteReader.GetDynamicPacenoteRecord(pacenoteType, pacenoteModifier, pacenoteDistance);
                    if (record != null)
                    {
                        records.Add(record);
                    }
                }
            }
            return ScriptReader.ReadFromDynamicPacenoteRecords(records);
        }
    }
}
