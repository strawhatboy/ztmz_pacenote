using System.Text;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

public sealed class WrcGenerationsPrerequisiteChecker : IGamePrerequisiteChecker
{
    private readonly IGameDocumentsLocator locator;
    public WrcGenerationsPrerequisiteChecker() : this(GameDocumentsLocatorFactory.CreateDefault()) { }
    public WrcGenerationsPrerequisiteChecker(IGameDocumentsLocator locator) => this.locator = locator;

    public string? FindSettingsFile() => locator.FindDocumentsDirectories(1953520)
        .SelectMany(directory => new[] { "UserSettings.cfg", "UserSetting.cfg" }
            .Select(name => Path.Combine(directory, "My Games", "WRCG", name)))
        .FirstOrDefault(File.Exists);

    public PrerequisitesCheckResult CheckPrerequisites(IGame game)
    {
        if (game.GameConfigurations.TryGetValue(WrcGenerationsConfig.Name, out var bridge) &&
            bridge is WrcGenerationsConfig { ReceiveForwardedTelemetry: true }) return Result(PrerequisitesCheckResultCode.OK);
        var file = FindSettingsFile();
        if (file is null) return Result(PrerequisitesCheckResultCode.GAME_NOT_INSTALLED);
        if (!game.GameConfigurations.TryGetValue(UdpGameConfig.Name, out var config) || config is not UdpGameConfig udp)
            return Result(PrerequisitesCheckResultCode.SETUP_REQUIRED);
        try
        {
            var text = File.ReadAllText(file);
            // Validate structure as well as values; never overwrite ambiguous duplicate assignments.
            _ = WrcGenerationsTelemetrySettings.Configure(text, udp.IPAddress, udp.Port);
            return Result(WrcGenerationsTelemetrySettings.Matches(text, udp.IPAddress, udp.Port)
                ? PrerequisitesCheckResultCode.OK : PrerequisitesCheckResultCode.PORT_NOT_OPEN);
        }
        catch (InvalidDataException) { return Result(PrerequisitesCheckResultCode.CONFIG_FILE_ABNORMAL); }
        catch (IOException) { return Result(PrerequisitesCheckResultCode.UNKNOWN); }
        catch (UnauthorizedAccessException) { return Result(PrerequisitesCheckResultCode.UNKNOWN); }
    }

    public void ForceFix(IGame game)
    {
        if (game.GameConfigurations.TryGetValue(WrcGenerationsConfig.Name, out var bridge) &&
            bridge is WrcGenerationsConfig { ReceiveForwardedTelemetry: true }) return;
        if (game.IsRunning) throw new InvalidOperationException("Close WRC Generations before changing telemetry settings.");
        var file = FindSettingsFile() ?? throw new FileNotFoundException("WRCG user settings not found.");
        var udp = (UdpGameConfig)game.GameConfigurations[UdpGameConfig.Name];
        string text;
        Encoding encoding;
        using (var reader = new StreamReader(file, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
        }
        var updated = WrcGenerationsTelemetrySettings.Configure(text, udp.IPAddress, udp.Port);
        if (updated == text) return;
        var temporary = file + ".ztmz-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, updated, encoding);
            File.Replace(temporary, file, file + ".ztmz-" + Guid.NewGuid().ToString("N") + ".bak");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static PrerequisitesCheckResult Result(PrerequisitesCheckResultCode code) =>
        new() { Code = code, IsOK = code == PrerequisitesCheckResultCode.OK };
}
