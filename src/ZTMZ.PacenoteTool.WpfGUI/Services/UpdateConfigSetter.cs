using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

public sealed class UpdateConfigSetter
{
    private readonly UpdateConfigurationMigrationService _migrationService;

    public UpdateConfigSetter(UpdateConfigurationMigrationService migrationService)
    {
        _migrationService = migrationService;
    }

    public void SetConfiguration(ZTMZPacenoteTool tool) =>
        _migrationService.Apply(tool);
}
