param(
    [Parameter(Mandatory = $true)][string]$NativeOutputDirectory,
    [Parameter(Mandatory = $true)][string[]]$HostOutputDirectory
)
$ErrorActionPreference = 'Stop'
$nativeRoot = (Resolve-Path -LiteralPath $NativeOutputDirectory).Path
$library = Join-Path $nativeRoot 'ztmz_dashboard_overlay.dll'
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($library)
if ($version.FileVersion -ne '1.0.0.0' -or $version.ProductName -ne 'ZTMZ Native Dashboard') {
    throw 'Expected the versioned production native dashboard DLL.'
}
foreach ($hostDirectory in $HostOutputDirectory) {
    $hostRoot = (Resolve-Path -LiteralPath $hostDirectory).Path
    if (!(Test-Path -LiteralPath (Join-Path $hostRoot 'ZTMZ.PacenoteTool.Platform.Windows.dll'))) {
        throw "Not a built Windows desktop host: $hostRoot"
    }
    foreach ($font in @('WRCCleanRoman.ttf', 'WRCCleanBold.ttf')) {
        if (!(Test-Path -LiteralPath (Join-Path $hostRoot "fonts/$font") -PathType Leaf)) {
            throw "Shared host font is missing: $hostRoot/fonts/$font. Build the host first."
        }
    }
    $destination = Join-Path $hostRoot 'native-dashboard/win-x64'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath $library -Destination $destination -Force
    Write-Host "Staged native dashboard DLL in $destination (using shared host fonts)"
}
