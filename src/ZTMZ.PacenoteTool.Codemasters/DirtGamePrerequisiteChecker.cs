using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Codemasters;

public class DirtGamePrerequisiteChecker : IGamePrerequisiteChecker
{
    private const int XmlLoadAttempts = 3;
    private const int XmlLoadRetryDelayMilliseconds = 100;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly string _dr1SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games",
        "DiRT Rally",
        "hardwaresettings",
        "hardware_settings_config.xml");
    private readonly string _dr1SettingsVrFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games",
        "DiRT Rally",
        "hardwaresettings",
        "hardware_settings_config_vr.xml");
    private readonly string _dr2SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games",
        "DiRT Rally 2.0",
        "hardwaresettings",
        "hardware_settings_config.xml");
    private readonly string _dr2SettingsVrFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games",
        "DiRT Rally 2.0",
        "hardwaresettings",
        "hardware_settings_config_vr.xml");

    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!TryGetSettingsFiles(game, out var settingsFile, out var settingsVrFile))
        {
            return Failure(PrerequisitesCheckResultCode.UNKNOWN);
        }

        var existingSettingsFiles = new[] { settingsFile, settingsVrFile }
            .Where(File.Exists)
            .ToList();

        if (existingSettingsFiles.Count == 0)
        {
            return Failure(PrerequisitesCheckResultCode.GAME_NOT_INSTALLED);
        }

        foreach (var file in existingSettingsFiles)
        {
            try
            {
                var result = Check(game, file);
                if (!result.IsOK)
                {
                    return result;
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Error while checking prerequisites for {0} at {1}", game.Name, file);
                return Failure(PrerequisitesCheckResultCode.UNKNOWN, game.Name, file);
            }
        }

        return Success();
    }

    public void ForceFix(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!TryGetSettingsFiles(game, out var settingsFile, out var settingsVrFile))
        {
            return;
        }

        if (!TryGetUdpConfig(game, out var udpConfig))
        {
            throw new InvalidOperationException($"Game {game.Name} has no valid UDP configuration.");
        }

        foreach (var file in new[] { settingsFile, settingsVrFile }.Where(File.Exists))
        {
            Write(file, udpConfig);
        }
    }

    public PrerequisitesCheckResult Check(IGame game, string file)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!TryGetUdpConfig(game, out var udpConfig))
        {
            Logger.Error("Game {0} has no valid UDP configuration.", game.Name);
            return Failure(PrerequisitesCheckResultCode.UNKNOWN, game.Name, file);
        }

        var xmlFile = LoadXml(file);
        if (xmlFile == null)
        {
            return Failure(PrerequisitesCheckResultCode.CONFIG_FILE_CORRUPTED, game.Name, file);
        }

        var motionPlatformNode = FindMotionPlatformNode(xmlFile);
        if (motionPlatformNode == null)
        {
            return Failure(PrerequisitesCheckResultCode.PORT_NOT_OPEN, game.Name, file);
        }

        var matchingNodes = GetUdpNodes(motionPlatformNode)
            .Where(node => MatchesUdpConfiguration(node, udpConfig))
            .ToList();

        if (matchingNodes.Count > 1)
        {
            Logger.Warn(
                "Found {0} matching UDP entries in {1}. Continuing because DiRT supports multiple UDP targets.",
                matchingNodes.Count,
                file);
        }

        return matchingNodes.Count > 0
            ? Success()
            : Failure(PrerequisitesCheckResultCode.PORT_NOT_OPEN, game.Name, file);
    }

    private bool TryGetSettingsFiles(IGame game, out string settingsFile, out string settingsVrFile)
    {
        if (string.Equals(game.Name, DirtRally.GameName, StringComparison.Ordinal))
        {
            settingsFile = _dr1SettingsFile;
            settingsVrFile = _dr1SettingsVrFile;
            return true;
        }

        if (string.Equals(game.Name, DirtRally2.GameName, StringComparison.Ordinal))
        {
            settingsFile = _dr2SettingsFile;
            settingsVrFile = _dr2SettingsVrFile;
            return true;
        }

        settingsFile = string.Empty;
        settingsVrFile = string.Empty;
        return false;
    }

    private static bool TryGetUdpConfig(IGame game, out UdpGameConfig udpConfig)
    {
        if (game.GameConfigurations != null &&
            game.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var configuration) &&
            configuration is UdpGameConfig configuredUdp)
        {
            udpConfig = configuredUdp;
            return true;
        }

        udpConfig = null!;
        return false;
    }

    private XDocument? LoadXml(string file)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < XmlLoadAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            }
            catch (Exception exception) when (exception is IOException or XmlException)
            {
                lastException = exception;
                if (attempt + 1 < XmlLoadAttempts)
                {
                    Thread.Sleep(XmlLoadRetryDelayMilliseconds);
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Error while loading XML file {0}", file);
                return null;
            }
        }

        Logger.Error(lastException!, "Error while loading XML file {0}", file);
        return null;
    }

    private static XElement? FindMotionPlatformNode(XDocument document)
    {
        return document.Root?.Elements().FirstOrDefault(node => IsElementNamed(node, "motion_platform"));
    }

    private static IEnumerable<XElement> GetUdpNodes(XElement motionPlatformNode)
    {
        return motionPlatformNode.Elements().Where(node => IsElementNamed(node, "udp"));
    }

    private static bool MatchesUdpConfiguration(XElement node, UdpGameConfig udpConfig)
    {
        var portValue = GetAttributeValue(node, "port");
        return IsEnabled(node) &&
               string.Equals(GetAttributeValue(node, "extradata")?.Trim(), "3", StringComparison.Ordinal) &&
               int.TryParse(portValue?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) &&
               port == udpConfig.Port;
    }

    private static bool IsEnabled(XElement node)
    {
        var value = GetAttributeValue(node, "enabled")?.Trim();
        return bool.TryParse(value, out var enabled)
            ? enabled
            : string.Equals(value, "1", StringComparison.Ordinal);
    }

    private static string? GetAttributeValue(XElement node, string name)
    {
        return node.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))
            ?.Value;
    }

    private static bool IsElementNamed(XElement node, string name)
    {
        return string.Equals(node.Name.LocalName, name, StringComparison.Ordinal);
    }

    private void Write(string file, UdpGameConfig udpConfig)
    {
        var xmlFile = LoadXml(file);
        if (xmlFile?.Root == null)
        {
            Logger.Error("Error while loading XML file {0} when force fixing the game configuration", file);
            throw new InvalidOperationException($"Error while loading XML file {file}");
        }

        var motionPlatformNode = FindMotionPlatformNode(xmlFile);
        if (motionPlatformNode == null)
        {
            motionPlatformNode = new XElement(xmlFile.Root.Name.Namespace + "motion_platform");
            xmlFile.Root.Add(motionPlatformNode);
        }

        if (GetUdpNodes(motionPlatformNode).Any(node => MatchesUdpConfiguration(node, udpConfig)))
        {
            Logger.Info("A matching UDP entry already exists in {0}; no configuration change was made.", file);
            return;
        }

        motionPlatformNode.Add(
            new XElement(
                motionPlatformNode.Name.Namespace + "udp",
                new XAttribute("enabled", "true"),
                new XAttribute("extradata", "3"),
                new XAttribute("ip", udpConfig.IPAddress),
                new XAttribute("port", udpConfig.Port.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("delay", "1")));

        xmlFile.Save(file, SaveOptions.DisableFormatting);
    }

    private static PrerequisitesCheckResult Success()
    {
        return new PrerequisitesCheckResult
        {
            Code = PrerequisitesCheckResultCode.OK,
            IsOK = true
        };
    }

    private static PrerequisitesCheckResult Failure(PrerequisitesCheckResultCode code, params object[] parameters)
    {
        return new PrerequisitesCheckResult
        {
            Code = code,
            IsOK = false,
            Params = parameters.Length > 0 ? new List<object>(parameters) : new List<object>()
        };
    }
}
