using System;
using System.Runtime.Versioning;
using Microsoft.Win32;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsRegistryConfigurationLocation : IConfigurationLocation
{
    private const string AppRegistryPath = @"Software\ZTMZ Next Generation Pacenote Tool";
    private const string AppConfigFolderRegistryValueName = "ZTMZHome";

    public string? GetConfiguredDataRoot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AppRegistryPath);
        return key?.GetValue(AppConfigFolderRegistryValueName) as string;
    }

    public void SetConfiguredDataRoot(string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppRegistryPath);
        if (key == null)
        {
            throw new InvalidOperationException("The application registry key could not be opened.");
        }

        key.SetValue(AppConfigFolderRegistryValueName, path, RegistryValueKind.String);
    }
}
