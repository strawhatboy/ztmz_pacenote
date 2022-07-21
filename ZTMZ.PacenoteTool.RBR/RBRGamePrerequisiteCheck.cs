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

using System.Collections.Generic;
using System.IO;
using IniParser;
using IniParser.Model;
using Microsoft.Win32;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRGamePrerequisiteChecker : IGamePrerequisiteChecker
{

    public string RBRRootDir { set; get; } = "";
    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
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

        if (notInstalled)
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.GAME_NOT_INSTALLED };
        }

        // check port
        var parser = new FileIniDataParser();
        var iniFilePath = Path.Join(RBRRootDir, "RichardBurnsRally.ini");
        IniData data = parser.ReadFile(iniFilePath);
        var ngp = data["NGP"];
        if (ngp.ContainsKey("udpTelemetry"))
        {
            if (ngp["udpTelemetry"].Equals("1"))
            {
                if (ngp.ContainsKey("udpTelemetryPort"))
                {
                    var port = ngp["udpTelemetryPort"];
                    var configPort = ((UdpGameConfig)game.GameConfigurations[UdpGameConfig.Name]).Port.ToString();
                    if (port.Equals(configPort))
                    {
                        return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.OK };
                    } else
                    {
                        return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.PORT_NOT_MATCH, Msg = "Port not match",
                            Params = new List<object> { game.Name, iniFilePath, port, configPort }
                        };
                    }
                } 
            } 
        } 
        
        return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.PORT_NOT_OPEN, Msg = "Port not open" , 
            Params = new List<object> { game.Name, iniFilePath }
        };
    }

    public void ForceFix(IGame game)
    {
        var parser = new FileIniDataParser();
        IniData data = parser.ReadFile(Path.Join(RBRRootDir, "RichardBurnsRally.ini"));
        var ngp = data["NGP"];
        ngp["udpTelemetry"] = "1";
        ngp["udpTelemetryPort"] = "6776";
        ngp["udpTelemetryAddress"] = "127.0.0.1";
        parser.WriteFile(Path.Join(RBRRootDir, "RichardBurnsRally.ini"), data);
    }
}

