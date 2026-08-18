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
    private readonly IGameDocumentsLocator _documentsLocator;

    public WRCGamePrerequisiteChecker()
        : this(GameDocumentsLocatorFactory.CreateDefault())
    {
    }

    public WRCGamePrerequisiteChecker(IGameDocumentsLocator documentsLocator)
    {
        _documentsLocator = documentsLocator ?? throw new ArgumentNullException(nameof(documentsLocator));
    }

    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        if (!TryGetConfigFiles(out var configFile, out var channelFile))
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.GAME_NOT_INSTALLED };
        }

        var udpConfig = game.GameConfigurations[UdpGameConfig.Name] as UdpGameConfig;
        JObject? config = null;
        if (File.Exists(configFile))
        {
            try
            {
                config = JObject.Parse(File.ReadAllText(configFile));
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
                    game.Name, configFile
                }};
            }
        } else {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.UNKNOWN };
        }

        // check if scheme is correct
        var ztmzConfig = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "ztmz.json");
        var ztmzJsonConfig = JObject.Parse(ztmzConfig);
        if (!File.Exists(channelFile))
        {
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.PORT_NOT_OPEN, Params = new List<object>() {
                game.Name, configFile
            }};
        }

        var originalZtmzJsonConfig = JObject.Parse(File.ReadAllText(channelFile));
        if (compareConfigVersion(originalZtmzJsonConfig, ztmzJsonConfig))
        {   // we got new ztmz json config, force fix!
            return new PrerequisitesCheckResult { Code = PrerequisitesCheckResultCode.PORT_NOT_OPEN, Params = new List<object>() {
                game.Name, configFile
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
        if (!TryGetConfigFiles(out var configFile, out var channelFile))
        {
            throw new FileNotFoundException("EA SPORTS WRC telemetry configuration was not found.");
        }

        var config = JObject.Parse(File.ReadAllText(configFile));

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
                    { "frequencyHz", 120 },
                    { "bEnabled", true }
                });
            } else {
                // exist, update port and ip
                packet["port"] = udpConfig.Port;
                packet["ip"] = udpConfig.IPAddress;
                packet["bEnabled"] = true;
                packet["frequencyHz"] = 120;
            }
        }

        File.WriteAllText(configFile, config.ToString(Newtonsoft.Json.Formatting.Indented));

        // write ztmz.json
        var ztmzConfig = StringHelper.ReadContentFromResource(Assembly.GetExecutingAssembly(), "ztmz.json");
        Directory.CreateDirectory(Path.GetDirectoryName(channelFile)!);
        File.WriteAllText(channelFile, ztmzConfig);
    }

    private bool TryGetConfigFiles(out string configFile, out string channelFile)
    {
        foreach (var documentsDirectory in _documentsLocator.FindDocumentsDirectories(WRC.SteamAppId))
        {
            var telemetryDirectory = Path.Combine(
                documentsDirectory,
                "My Games",
                "WRC",
                "telemetry");
            var candidateConfigFile = Path.Combine(telemetryDirectory, "config.json");
            if (File.Exists(candidateConfigFile))
            {
                configFile = candidateConfigFile;
                channelFile = Path.Combine(telemetryDirectory, "udp", "ztmz.json");
                return true;
            }
        }

        configFile = string.Empty;
        channelFile = string.Empty;
        return false;
    }
}
