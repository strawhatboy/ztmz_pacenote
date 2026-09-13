$ErrorActionPreference = 'Stop'

function Get-ReleaseSha256([string]$Path) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::OpenRead($Path)
        try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose() }
    } finally { $hash.Dispose() }
}

# An ordinally sorted UTF-8 manifest binds both filenames and file contents.
# Use git archive, not checkout, to avoid machine-specific newline conversion.
function Get-ReleaseTreeDigest([string]$Root) {
    $rootPath = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    $entries = @(Get-ChildItem -LiteralPath $rootPath -Recurse -Force)
    foreach ($entry in $entries) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Linked resource entry is not allowed: $($entry.FullName)"
        }
    }
    [string[]]$lines = @($entries | Where-Object { -not $_.PSIsContainer } | ForEach-Object {
        $relative = $_.FullName.Substring($rootPath.Length + 1).Replace('\', '/')
        (Get-ReleaseSha256 $_.FullName) + '  ' + $relative
    })
    [Array]::Sort($lines, [StringComparer]::Ordinal)
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n")
        [pscustomobject]@{
            files = $lines.Count
            sha256 = [BitConverter]::ToString($hash.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
        }
    } finally { $hash.Dispose() }
}

function Assert-ReleaseTree([string]$Root, $Expected) {
    $actual = Get-ReleaseTreeDigest $Root
    if ($actual.files -ne $Expected.files -or $actual.sha256 -ne $Expected.sha256) {
        throw "Resource contents differ from release-resources.json: $Root (files=$($actual.files), sha256=$($actual.sha256)). Run publish again from the pinned sources."
    }
    Write-Host "Verified $Root : $($actual.files) files"
}
