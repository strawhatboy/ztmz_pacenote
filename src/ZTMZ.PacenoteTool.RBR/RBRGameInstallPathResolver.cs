using System;
using System.IO;
using Microsoft.Win32;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR;

internal enum RBRInstallationPathSource
{
    None,
    ConfiguredExecutable,
    Registry
}

internal static class RBRGameInstallPathResolver
{
    private const string RegistryPath = @"Software\Wow6432Node\Rallysimfans RBR";
    private const string RegistryInstallPathValue = "InstallPath";

    private static readonly string[] SupportedExecutableNames =
    [
        "RichardBurnsRally_SSE.exe",
        "RichardBurnsRally.exe"
    ];

    public static bool TryResolveRoot(
        IGame game,
        out string rootDirectory,
        out RBRInstallationPathSource source)
    {
        game.GameConfigurations.TryGetValue(RBRGameInstallationConfig.Name, out var configuration);
        return TryResolveRoot(configuration as RBRGameInstallationConfig, out rootDirectory, out source);
    }

    public static bool TryResolveRoot(
        RBRGameInstallationConfig? configuration,
        out string rootDirectory,
        out RBRInstallationPathSource source)
    {
        if (TryGetConfiguredRoot(configuration?.ExecutablePath, out rootDirectory))
        {
            source = RBRInstallationPathSource.ConfiguredExecutable;
            return true;
        }

        if (TryGetRegistryRoot(out rootDirectory))
        {
            source = RBRInstallationPathSource.Registry;
            return true;
        }

        rootDirectory = string.Empty;
        source = RBRInstallationPathSource.None;
        return false;
    }

    public static bool TryGetConfiguredRoot(string? executablePath, out string rootDirectory)
    {
        rootDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(executablePath.Trim());
            if (!File.Exists(fullPath) ||
                !Array.Exists(SupportedExecutableNames, name =>
                    string.Equals(name, Path.GetFileName(fullPath), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            rootDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(rootDirectory);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryGetRegistryRoot(out string rootDirectory)
    {
        rootDirectory = string.Empty;
        using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);
        var installPath = key?.GetValue(RegistryInstallPathValue) as string;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return false;
        }

        try
        {
            rootDirectory = Path.GetFullPath(installPath.Trim());
            return true;
        }
        catch (Exception)
        {
            rootDirectory = string.Empty;
            return false;
        }
    }
}
