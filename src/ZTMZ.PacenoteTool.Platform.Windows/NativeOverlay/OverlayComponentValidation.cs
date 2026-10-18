using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace ZTMZ.PacenoteTool.Platform.Windows.NativeOverlay;

internal sealed class OverlayComponentException(string path, Exception inner)
    : IOException($"Cannot use dashboard component '{path}': {inner.Message}", inner)
{
    public string ComponentPath { get; } = path;
}

internal static class OverlayComponentValidation
{
    public static Machine ValidateBundle(string libraryPath)
    {
        var machine = ValidateFile(libraryPath, true);
        if (machine == Machine.I386)
            ValidateFile(Path.Combine(Path.GetDirectoryName(libraryPath)!, "ztmz_dashboard_injector.exe"), false);
        return machine;
    }

    private static Machine ValidateFile(string path, bool library)
    {
        try
        {
            // Opening rather than File.Exists preserves access-denied/security error codes.
            using var file = File.OpenRead(path);
            using var pe = new PEReader(file);
            var header = pe.PEHeaders.CoffHeader;
            if (library)
            {
                if (header.Machine is not (Machine.Amd64 or Machine.I386) ||
                    !header.Characteristics.HasFlag(Characteristics.Dll))
                    throw new BadImageFormatException("Overlay module must be an x86 or x64 DLL.");
                var version = FileVersionInfo.GetVersionInfo(path);
                if (version.FileVersion != "1.0.0.0" || version.ProductName != "ZTMZ Native Dashboard")
                    throw new InvalidDataException("Unsupported overlay module version; repair the native dashboard bundle.");
            }
            else if (header.Machine != Machine.I386 || header.Characteristics.HasFlag(Characteristics.Dll))
                throw new BadImageFormatException("The WOW64 loader helper must be an x86 executable.");
            return header.Machine;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                                      BadImageFormatException or System.ComponentModel.Win32Exception)
        {
            throw new OverlayComponentException(path, error);
        }
    }
}
