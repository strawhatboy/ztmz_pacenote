//
// UDP Configuretion File: [rbrRoot]\RichardBurnsRally.ini  
//
// [NGP]
// udpTelemetry=1
// udpTelemetryAddress=127.0.0.1
// udpTelemetryPort=6776
//
// 
// 1. check if the game was installed by registry
// 2. check if the udpTelemetry is 1 (port opened)
// 3. check if the Port matches that in game configuration
//

using System;
using System.Collections.Generic;
using System.IO;
using IniParser;
using IniParser.Model;
using Microsoft.Win32;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRGamePrerequisiteChecker : IGamePrerequisiteChecker
{
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public string RBRRootDir { set; get; } = "";
    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        _logger.Info("Checking RBR Prerequisites");

        bool notInstalled = false;
        var key = Registry.LocalMachine.OpenSubKey("Software\\Wow6432Node\\Rallysimfans RBR");
        if (key != null)
        {
            var rootDir = key.GetValue("InstallPath") as string;
            if (rootDir != null) 
            {
                RBRRootDir = rootDir;
            } else {
                notInstalled = true;
            }
        } else 
        {
            // HU rbr not installed.
            notInstalled = true;
        }

        var iniFilePath = Path.Join(RBRRootDir, "RichardBurnsRally.ini");
        if (!checkIfIniFileValid(iniFilePath)) 
        {
            notInstalled = true;
        }

        var trackFilePath = Path.Join(RBRRootDir, "Maps\\Tracks.ini");
        if (!checkIfIniFileValid(trackFilePath)) 
        {
            notInstalled = true;
        }

        if (notInstalled)
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.GAME_NOT_INSTALLED };
        }

        // check port, we don't use UDP anymore, no more port checking.
        return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.OK };
    }

    public void ForceFix(IGame game)
    {
        var parser = new FileIniDataParser();
        var iniFilePath = Path.Join(RBRRootDir, "RichardBurnsRally.ini");
        IniData data = parser.ReadFile(iniFilePath);
        var ngp = data["NGP"];
        ngp["udpTelemetry"] = "1";
        ngp["udpTelemetryAddress"] = "127.0.0.1";
        ngp["udpTelemetryPort"] = "6776";
        // Shit! why this breaks the game? because it's using UTF8 as defaut, but not system default...
        // so create bak incase...
        var bakFilePath = Path.Join(RBRRootDir, "RichardBurnsRally.ini.bak");
        if (!File.Exists(bakFilePath))
        {
            File.Copy(iniFilePath, bakFilePath);
        }
        parser.WriteFile(Path.Join(RBRRootDir, "RichardBurnsRally.ini"), data, System.Text.Encoding.Default);
    }

    private bool checkIfIniFileValid(string iniFilePath)
    {
        if (!File.Exists(iniFilePath)) 
        {
            // RSF rbr not valid or uninstalled properly.
            return false;
        } else {
            try {
                var parser = new FileIniDataParser();
                IniData data = parser.ReadFile(iniFilePath);
            } catch {
                // RSF rbr not valid or uninstalled properly.
                return false;
            }
        }
        return true;
    }
}

