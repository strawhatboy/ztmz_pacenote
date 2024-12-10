using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRMemDataReader
{
    private bool _isProcessOpened = false;
    private Process _theProcess = null;

    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public bool OpenProcess(IGame game)
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
        rbrProcess.Refresh();
        if (rbrProcess.HasExited)
        {
            _logger.Error("RBR process has exited.");
            return false;
        }

        try 
        {
            MemoryReader.OpenProcess(16, false, rbrProcess.Id);
            _theProcess = rbrProcess;
            _isProcessOpened = true;
        } catch (Exception ex)
        {
            _logger.Error("Failed to open RBR process");
            _logger.Error(ex.Message);
            return false;
        }

        _logger.Info("RBR process opened.");
        
        return true;
    }

    public RBRMemData ReadMemData(IGame game)
    {
        if (!OpenProcess(game))
        {
            return default(RBRMemData);
        }

        var pHandle = _theProcess.Handle;
        var memData = new RBRMemData();
        memData.IsRunning = true;
        memData.IsRendering = MemoryReader.Read<byte>(pHandle, 0x7C0BBC) > 0;
        memData.GameStateId = MemoryReader.Read<byte>(pHandle, 0x7EAC48, 0x728);

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

        return memData;
    }
    public string GetTrackNameFromMemory()
    {
        if (!_isProcessOpened)
        {
            return "";
        }
        var pHandle = _theProcess.Handle;
        int trackNameAddr = MemoryReader.Read<int>(pHandle, 0x4A1123);
        if (trackNameAddr != 0x731234 && trackNameAddr != 0)
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

    public void CloseProgress()
    {
        if (_isProcessOpened)
        {
            MemoryReader.CloseHandle(_theProcess.Handle);
            _isProcessOpened = false;
        }
    }

    public ScriptReader ReadPacenotesFromMemory()
    {
        if (!_isProcessOpened)
        {
            return null;
        }
        var pHandle = _theProcess.Handle;
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
