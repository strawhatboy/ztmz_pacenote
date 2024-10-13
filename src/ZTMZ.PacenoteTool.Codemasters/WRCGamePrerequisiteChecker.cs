using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

public class WRCGamePrerequisiteChecker : IGamePrerequisiteChecker
{
    public static string WRCUDPConfigFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/WRC/telemetry/config.json");
    public static string WRCUDPZTMZChannelFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games/WRC/telemetry/udp/ztmz.json");
    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        var udpConfig = game.GameConfigurations[UdpGameConfig.Name] as UdpGameConfig;
        JObject? config = null;
        if (File.Exists(WRCUDPConfigFile))
        {
            try
            {
                config = JObject.Parse(File.ReadAllText(WRCUDPConfigFile));
            }
            catch (Exception)
            {
                return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN };
            }
        }
        else
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.GAME_NOT_INSTALLED };
        }

        if (config == null)
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN };
        }

        var udpNode = config["udp"];
        if (udpNode == null)
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN };
        }

        var packetsNode = udpNode["packets"];
        if (packetsNode == null || packetsNode.Type != JTokenType.Array)
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN };
        }
        
        if (packetsNode is JArray packets) {
        // find if exists packetobject's property "structure" equals "ztmz" and "packet" equals "session_update"
            var packet = packets.FirstOrDefault(p => p["structure"]?.ToString() == "ztmz" && p["packet"]?.ToString() == "session_update");
            if (packet == null || 
                (udpConfig != null && packet["port"]?.ToString() != udpConfig.Port.ToString())) // has udp config but port not match
            {
                return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.PORT_NOT_OPEN, Params = new List<object>() {
                    game.Name, WRCUDPConfigFile
                }};
            }
        } else {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN };
        }

        // check if scheme is correct
        var ztmzConfig = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "ztmz.json");
        var ztmzJsonConfig = JObject.Parse(ztmzConfig);
        var originalZtmzJsonConfig = JObject.Parse(File.ReadAllText(WRCUDPZTMZChannelFile));
        if (compareConfigVersion(originalZtmzJsonConfig, ztmzJsonConfig))
        {   // we got new ztmz json config, force fix!
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.PORT_NOT_OPEN, Params = new List<object>() {
                game.Name, WRCUDPConfigFile
            }};
        }
        
        return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.OK };
    }

    private bool compareConfigVersion(JObject config, JObject ztmzJsonConfig)
    {
        var configVersion = getVersionFromConfig(config);
        var ztmzJsonConfigVersion = getVersionFromConfig(ztmzJsonConfig);
        return configVersion < ztmzJsonConfigVersion;
    }

    private Version getVersionFromConfig(JObject config)
    {
        var version = config["versions"];
        var scheme = version["schema"].Value<int>();
        var data = version["data"].Value<int>();
        return new Version(scheme, data);
    }

    public void ForceFix(IGame game)
    {// only when PORT_NOT_OPEN
        var config = JObject.Parse(File.ReadAllText(WRCUDPConfigFile));

        var packetsNode = config["udp"]["packets"];
        
        var udpConfig = game.GameConfigurations[UdpGameConfig.Name] as UdpGameConfig;

        if (packetsNode is JArray packets && udpConfig != null) {
            var packet = packets.FirstOrDefault(p => p["structure"]?.ToString() == "ztmz" && p["packet"]?.ToString() == "session_update");
            if (packet == null)
            {
                // add packetobject
                packets.Add(new JObject {
                    { "structure", "ztmz" },
                    { "packet", "session_update" },
                    { "ip", udpConfig.IPAddress },
                    { "port", udpConfig.Port },
                    { "frequencyHz", 60 },
                    { "bEnabled", true }
                });
            } else {
                // exist, update port and ip
                packet["port"] = udpConfig.Port;
                packet["ip"] = udpConfig.IPAddress;
                packet["bEnabled"] = true;
                packet["frequencyHz"] = 60;
            }
        }

        File.WriteAllText(WRCUDPConfigFile, config.ToString(Newtonsoft.Json.Formatting.Indented));

        // write ztmz.json
        var ztmzConfig = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "ztmz.json");
        File.WriteAllText(WRCUDPZTMZChannelFile, ztmzConfig);
    }
}
