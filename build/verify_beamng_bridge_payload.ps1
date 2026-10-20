param([Parameter(Mandatory = $true)][string]$ModRoot)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release_resource_helpers.ps1')
$source = (Resolve-Path (Join-Path $PSScriptRoot '../src/ZTMZ.PacenoteTool.BeamNG/BeamNGMod')).Path
$destination = (Resolve-Path -LiteralPath $ModRoot).Path
$files = @(Get-ChildItem -LiteralPath $source -Recurse -File)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length + 1)
    $published = Join-Path $destination $relative
    if (-not (Test-Path -LiteralPath $published -PathType Leaf)) {
        throw "Missing bundled BeamNG bridge file: $relative"
    }
    if ((Get-ReleaseSha256 $published) -ne (Get-ReleaseSha256 $file.FullName)) {
        throw "Stale bundled BeamNG bridge file: $relative"
    }
}
foreach ($file in Get-ChildItem -LiteralPath $destination -Recurse -File) {
    $relative = $file.FullName.Substring($destination.Length + 1)
    if (-not (Test-Path -LiteralPath (Join-Path $source $relative) -PathType Leaf)) {
        throw "Unexpected bundled BeamNG bridge file: $relative"
    }
}
$metadata = Get-Content -LiteralPath (Join-Path $destination 'mod_info/ZTMZPACENOTERALLYBRIDGE/info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Host "BeamNG bridge $($metadata.version_string) verified: $($files.Count) files match source."
