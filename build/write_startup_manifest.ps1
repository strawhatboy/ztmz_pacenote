param([Parameter(Mandatory = $true)][string]$DestinationRoot)
$ErrorActionPreference = 'Stop'
$root = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationRoot)
$files = @(
    'ZTMZ.PacenoteTool.WpfGUI.exe', 'ZTMZ.PacenoteTool.WpfGUI.dll',
    'ZTMZ.PacenoteTool.WpfGUI.deps.json', 'ZTMZ.PacenoteTool.WpfGUI.runtimeconfig.json',
    'fonts/WRCCleanRoman.ttf', 'fonts/WRCCleanBold.ttf',
    'Assets/PacenoteIcons/v1/manifest.json', 'Assets/Monaco/index.html'
)
# Framework-dependent development output keeps native libraries under runtimes/.
$files += $(if (Test-Path -LiteralPath (Join-Path $root 'WebView2Loader.dll')) { 'WebView2Loader.dll' } else { 'runtimes/win-x64/native/WebView2Loader.dll' })
$deps = Get-Content -LiteralPath (Join-Path $root 'ZTMZ.PacenoteTool.WpfGUI.deps.json') -Raw | ConvertFrom-Json
$target = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value
foreach ($library in $target.PSObject.Properties) {
    foreach ($kind in @('runtime', 'native')) {
        if ($null -eq $library.Value.$kind) { continue }
        foreach ($asset in $library.Value.$kind.PSObject.Properties) {
            if ($asset.Name.EndsWith('/_._')) { continue }
            $files += [IO.Path]::GetFileName($asset.Name)
        }
    }
    # Satellite resources are intentionally excluded, as in the installer.
}
foreach ($provider in @('Codemasters', 'RBR', 'BeamNG', 'AC', 'KT')) {
    $files += "games/ZTMZ.PacenoteTool.$provider.dll"
}
$manifest = [ordered]@{
    schemaVersion = 1
    applicationFiles = @($files | Sort-Object -Unique)
    dataFiles = @('data.zdb')
}
[IO.File]::WriteAllText((Join-Path $root 'ZTMZ.startup.json'), ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
