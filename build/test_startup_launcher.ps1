param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot 'startup-launcher'),
    [string]$PublishedRoot = (Join-Path $PSScriptRoot 'target/publish/ZTMZ.PacenoteTool.WpfGUI/release_win-x64')
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot ('../tmp/startup-integration-' + [Guid]::NewGuid().ToString())
$root = [IO.Path]::GetFullPath($root)
$app = Join-Path $root 'app with spaces'
$data = Join-Path $root 'data'
$null = New-Item $app, $data -ItemType Directory -Force
Copy-Item (Join-Path $BuildDirectory 'Release/startup_launcher_fixture.exe') (Join-Path $app 'ZTMZ.Launcher.exe')
Copy-Item (Join-Path $BuildDirectory 'Release/startup_child_fixture.exe') (Join-Path $app 'ZTMZ.PacenoteTool.WpfGUI.exe')
foreach ($file in @('WebView2Loader.dll', 'ZTMZ.PacenoteTool.WpfGUI.runtimeconfig.json')) {
    Copy-Item (Join-Path $PublishedRoot $file) $app
}
[IO.File]::WriteAllText((Join-Path $data 'data.zdb'), 'fixture only; never opened by the real application')
$manifestPath = Join-Path $app 'ZTMZ.startup.json'
$manifest = '{"schemaVersion":1,"applicationFiles":["ZTMZ.PacenoteTool.WpfGUI.exe"],"dataFiles":["data.zdb"]}'
[IO.File]::WriteAllText($manifestPath, $manifest)
$savedData = $env:ZTMZ_DATA_HOME
$savedRuntime = $env:DOTNET_ROOT_X64
$script:checks = 0
function Assert-Equal($Actual, $Expected, [string]$Message) {
    if ($Actual -ne $Expected) { throw "$Message -- expected '$Expected', got '$Actual'" }
    $script:checks++
}
function Run-Case([string]$Name, [string]$Expected, [string]$Mode = '', [switch]$Diagnose) {
    $reportPath = Join-Path $root ($Name + '.json')
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $app 'ZTMZ.Launcher.exe'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $timeout = if ($Expected -eq 'slow') { 5 } else { 10 }
    $start.Arguments = '--headless --timeout-seconds ' + $timeout + ' --report "' + $reportPath + '" ' + $Mode
    if ($Diagnose) { $start.Arguments += ' --diagnose' }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(20000)) { throw "Launcher did not finish case $Name" }
        $report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Equal $report.state $Expected $Name
        if ($Expected -eq 'ready') {
            $child = Get-Process -Id $report.childProcessId -ErrorAction Stop
            Assert-Equal $child.HasExited $false 'launcher must exit without terminating the ready child'
            Assert-Equal $report.managedStartup.arguments[0] 'two words' 'argument forwarding'
        }
        if ($Expected -eq 'slow') {
            $child = Get-Process -Id $report.childProcessId -ErrorAction Stop
            Assert-Equal $child.HasExited $false 'timeout must leave child running'
            $childStatus = Get-Content -LiteralPath (Join-Path $report.sessionFolder 'managed-status.json') -Raw | ConvertFrom-Json
            if ($Mode -eq 'stale') { Assert-Equal $childStatus.pid 0 'reject a status from the wrong PID' }
            else { Assert-Equal $report.managedStartup.state 'starting' 'timeout while actual initialization is in progress' }
        }
        Write-Host "PASS $Name ($Expected)"
    } finally {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
        if (Test-Path -LiteralPath $reportPath) {
            $report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($report.childProcessId) {
                $ownedChild = Get-Process -Id $report.childProcessId -ErrorAction SilentlyContinue
                if ($ownedChild -and $ownedChild.Path -eq (Join-Path $app 'ZTMZ.PacenoteTool.WpfGUI.exe')) {
                    Stop-Process -Id $ownedChild.Id
                }
            }
        }
    }
}
try {
    $env:ZTMZ_DATA_HOME = Join-Path $root 'missing'
    Run-Case 'missing-configured-folder' 'data_folder' -Diagnose
    Assert-Equal (Test-Path $env:ZTMZ_DATA_HOME) $false 'do not create a replacement profile'
    $env:ZTMZ_DATA_HOME = $data
    $config = Join-Path $data 'userconfig.json'
    [IO.File]::WriteAllText($config, '{broken')
    Run-Case 'malformed-configuration' 'configuration' -Diagnose
    Assert-Equal ([IO.File]::ReadAllText($config)) '{broken' 'preflight must not reset settings'
    [IO.File]::WriteAllText($config, '{}')
    [IO.File]::WriteAllText((Join-Path $data 'config.json'), "{'Language':'en-us',}")
    Run-Case 'legacy-jsonnet-syntax' 'checks_passed' -Diagnose
    [IO.File]::WriteAllText($manifestPath, '{"schemaVersion":1,"applicationFiles":["missing.dll"],"dataFiles":[]}')
    Run-Case 'missing-program-file' 'files' -Diagnose
    [IO.File]::WriteAllText($manifestPath, $manifest)
    $env:DOTNET_ROOT_X64 = Join-Path $root 'missing-runtime'
    Run-Case 'missing-dotnet' 'runtime' -Diagnose
    $env:DOTNET_ROOT_X64 = $savedRuntime
    Run-Case 'healthy-preflight' 'checks_passed' -Diagnose
    Run-Case 'ready-handshake' 'ready' 'ready "two words"'
    Run-Case 'managed-failure' 'startup_failed' 'failed'
    Run-Case 'exit-before-entry' 'startup_failed' 'exit'
    Run-Case 'slow-startup' 'slow' 'slow'
    Run-Case 'stale-process-status' 'slow' 'stale'
    Write-Host "$script:checks integration assertions passed. Reports: $root"
} finally {
    $env:ZTMZ_DATA_HOME = $savedData
    $env:DOTNET_ROOT_X64 = $savedRuntime
}
