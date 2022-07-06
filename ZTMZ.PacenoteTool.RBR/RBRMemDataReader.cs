using System;
using System.Diagnostics;
using System.Text;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRMemDataReader
{
    private bool _isProcessOpened = false;
    private Process _theProcess = null;

    public bool OpenProcess(IGame game)
    {
        if (_isProcessOpened)
        {
            return true;
        }
        
        var rbrProcesses = Process.GetProcessesByName(game.Executable.Remove(game.Executable.IndexOf(".exe")));
        if (rbrProcesses.Length == 0)
        {
            return false;
        }

        var rbrProcess = rbrProcesses[0];
        if (rbrProcess.HasExited)
        {
            return false;
        }

        try 
        {
            MemoryReader.OpenProcess(16, false, rbrProcess.Id);
            _theProcess = rbrProcess;
            _isProcessOpened = true;
        } catch (Exception ex)
        {
            Debug.WriteLine("Failed to open RBR process");
            Debug.WriteLine(ex.Message);
            return false;
        }
        
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

        if (rbrGameState == RBRGameState.Racing)
        {
            var baseAddr0 = MemoryReader.Read<int>(pHandle, 0x165FC68);
            // racedata available
            memData.SpeedKMH = MemoryReader.Read<float>(pHandle, baseAddr0 + 12);
            memData.EngineRPM = MemoryReader.Read<float>(pHandle, baseAddr0 + 16);
            memData.WaterTemperatureCelsius = MemoryReader.Read<float>(pHandle, baseAddr0 + 20);
            memData.TurboPressureBar = MemoryReader.Read<float>(pHandle, baseAddr0 + 24) / 100000f;
            memData.DistanceFromStart = MemoryReader.Read<float>(pHandle, baseAddr0 + 32);
            memData.TravelledDistance = MemoryReader.Read<float>(pHandle, baseAddr0 + 36);
            memData.DistanceToFinish = MemoryReader.Read<float>(pHandle, baseAddr0 + 40);
            memData.CurrentStagePosition = MemoryReader.Read<float>(pHandle, baseAddr0 + 0x13C);
            memData.RaceTime = MemoryReader.Read<float>(pHandle, baseAddr0 + 0x140);
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
}
