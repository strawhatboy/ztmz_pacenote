//
// UDP Configuretion File: [rbrRoot]\RichardBurnsRally.ini  
//
// [NGP]
// udpTelemetry=1
// udpTelemetryEndpoints=127.0.0.1
// udpTelemetryPort=6776
//
// 
// 1. resolve the configured RBR executable, then use the registry as a fallback
// 2. check if the udpTelemetry is 1 (port opened)
// 3. check if the Port matches that in game configuration
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IniParser;
using IniParser.Model;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRGamePrerequisiteChecker : IGamePrerequisiteChecker
{
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public string RBRRootDir { set; get; } = "";
    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        _logger.Info("Checking RBR Prerequisites");

        if (!RBRGameInstallPathResolver.TryResolveRoot(game, out var rootDirectory, out var source))
        {
            _logger.Info("RBR installation was not found in the configured executable path or registry.");
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.GAME_NOT_INSTALLED };
        }

        RBRRootDir = rootDirectory;
        _logger.Info("Resolved RBR installation at {0} from {1}.", RBRRootDir, source);

        var iniFilePath = Path.Join(RBRRootDir, "RichardBurnsRally.ini");
        if (!RBRHelper.checkIfIniFileValid(iniFilePath)) 
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.CONFIG_FILE_CORRUPTED, IsOK = false, Params = new List<object> { game.Name, iniFilePath } };
        }

        var trackFilePath = Path.Join(RBRRootDir, "Maps\\Tracks.ini");
        if (!RBRHelper.checkIfIniFileValid(trackFilePath)) 
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.CONFIG_FILE_CORRUPTED, IsOK = false, Params = new List<object> { game.Name, trackFilePath } };
        }

        // udp
        var parser = new FileIniDataParser();
        IniData data = parser.ReadFile(iniFilePath);
        var ngp = data["NGP"];
        var udpConfig = game.GameConfigurations[UdpGameConfig.Name] as UdpGameConfig;
        if (udpConfig == null) 
        {
            _logger.Error("UDP Configuration not found in game configurations.");
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN };
        }

        if ((ngp.ContainsKey("udpTelemetry") && ngp["udpTelemetry"] != "1") ||  // udpTelemetry not enabled
         (!ngp.ContainsKey("udpTelemetry")) ||  // no udpTelemetry config 
         (ngp.ContainsKey("udpTelemetry") && ngp["udpTelemetry"] == "1" &&  // udpTelemetryEndpoints doesnot include ztmz settings
        ngp.ContainsKey("udpTelemetryEndpoints") && !ngp["udpTelemetryEndpoints"].Contains(udpConfig.Port.ToString()))) {
            // port not open
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.PORT_NOT_OPEN, IsOK = false, Params = new List<object>{
                game.Name, iniFilePath
            } };
        }

        // check duplicated port opening
        if (ngp.ContainsKey("udpTelemetryEndpoints")) {
            var addresses = ngp["udpTelemetryEndpoints"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            bool hasConfig = false;
            foreach (var addr in addresses) {
                if (addr.Contains(udpConfig.Port.ToString())) {
                    if (hasConfig) {
                        return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.CONFIG_FILE_ABNORMAL, IsOK = false, Params = new List<object>{
                            game.Name, iniFilePath
                        } };
                    } else {
                        hasConfig = true;
                    }
                }
            }
        }

        return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.OK };
    }

    public void ForceFix(IGame game)
    {
        if (!RBRGameInstallPathResolver.TryResolveRoot(game, out var rootDirectory, out _))
        {
            _logger.Error("Cannot configure RBR telemetry because the RBR installation was not found.");
            return;
        }

        RBRRootDir = rootDirectory;
        var parser = new FileIniDataParser();
        var iniFilePath = Path.Join(RBRRootDir, "RichardBurnsRally.ini");
        IniData data = parser.ReadFile(iniFilePath);
        var ngp = data["NGP"];
        var udpConfig = game.GameConfigurations[UdpGameConfig.Name] as UdpGameConfig;
        if (udpConfig == null) 
        {
            _logger.Error("UDP Configuration not found in game configurations.");
            return;
        }
        ngp["udpTelemetry"] = "1";
        // append udp configuration
        var udpAddress = $"{udpConfig.IPAddress}:{udpConfig.Port}";
        if (ngp.ContainsKey("udpTelemetryEndpoints")) 
        {
            var originalAddress = ngp["udpTelemetryEndpoints"];
            var originalAddresses = originalAddress.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            // remove all entries with same port
            originalAddresses.RemoveAll(a => a.Contains(udpConfig.Port.ToString()));
            // then append
            originalAddresses.Add(udpAddress);
            ngp["udpTelemetryEndpoints"] = string.Join(',', originalAddresses);
        } else {
            ngp.AddKey("udpTelemetryEndpoints", udpAddress);
        }
        // Shit! why this breaks the game? because it's using UTF8 as defaut, but not system default...
        // so create bak incase...
        var bakFilePath = Path.Join(RBRRootDir, "RichardBurnsRally.ini.bak");
        if (!File.Exists(bakFilePath))
        {
            File.Copy(iniFilePath, bakFilePath);
        }
        parser.WriteFile(Path.Join(RBRRootDir, "RichardBurnsRally.ini"), data, System.Text.Encoding.Default);
    }


}
