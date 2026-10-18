#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base.Game;

/// <summary>A recoverable dashboard failure, independent of pacenote playback.</summary>
public sealed record OverlayFailureInfo(string StatusKey, string? ComponentPath, string Details)
{
    public string ToDisplayText() => string.Join(Environment.NewLine,
        new[] { I18NLoader.Instance[StatusKey], ComponentPath, Details }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public static OverlayFailureInfo FromException(Exception error, string? componentPath = null)
    {
        var key = "overlayBackend.loadFailed";
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            if (current is FileNotFoundException missing)
                componentPath ??= missing.FileName;
            var code = current is Win32Exception windows ? windows.NativeErrorCode : current.HResult & 0xffff;
            if (current is FileNotFoundException or DirectoryNotFoundException || code is 2 or 3)
            {
                key = "overlayBackend.componentMissing";
                break;
            }
            if (current is Win32Exception && code == 5 && componentPath == null)
            {
                key = "overlayBackend.gameAccessDenied";
                break;
            }
            if (current is UnauthorizedAccessException || code is 5 or 225 or 226 or 577)
            {
                key = "overlayBackend.componentBlocked";
                break;
            }
            if (current is BadImageFormatException or InvalidDataException || code == 193)
            {
                key = "overlayBackend.componentInvalid";
                break;
            }
        }
        return new(key, componentPath, error.ToString());
    }
}
