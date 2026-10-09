param([Parameter(Mandatory = $true)][string]$BuildDirectory,
    [ValidateSet('x64', 'x86')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../src/ZTMZ.PacenoteTool.NativeDashboard'
$BuildDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($BuildDirectory)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio 2022 C++ tools and CMake are required to build the native dashboard.' }
$installation = & $vswhere -latest -products '*' -version '[17.0,18.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'Install Visual Studio 2022 Desktop development with C++ (including Windows SDK and CMake).' }
$cmake = Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
if (!(Test-Path -LiteralPath $cmake)) {
    $cmake = (Get-Command cmake.exe -ErrorAction Stop).Source
}
# Both hosts share this incremental build tree; never run competing CMake builds.
$sha = [Security.Cryptography.SHA256]::Create()
try { $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($BuildDirectory.ToLowerInvariant()))).Replace('-', '') }
finally { $sha.Dispose() }
$mutex = New-Object Threading.Mutex($false, "Local\ZtmzNativeDashboard-$key")
$held = $false
try {
    try { $held = $mutex.WaitOne([TimeSpan]::FromMinutes(10)) }
    catch [Threading.AbandonedMutexException] { $held = $true }
    if (!$held) { throw 'Timed out waiting for the other native dashboard build.' }
    # Release /MT also for Debug hosts: no debug CRT or separately installed VC runtime.
    # Tests/viewer are not shipping artifacts.
    Write-Host "Building native dashboard $Architecture (auto: DX9, DX10, DX11, DX12, Vulkan; incremental Release)..."
    $cmakeArchitecture = if ($Architecture -eq 'x86') { 'Win32' } else { 'x64' }
    & $cmake -S $source -B $BuildDirectory -G 'Visual Studio 17 2022' -A $cmakeArchitecture "-DCMAKE_GENERATOR_INSTANCE=$installation" '-DBUILD_TESTING=OFF' '-DZTMZ_RENDER_BACKENDS=dx9;dx10;dx11;dx12;vulkan' '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded'
    if ($LASTEXITCODE -ne 0) { throw 'Native dashboard CMake configuration failed.' }
    & $cmake --build $BuildDirectory --config Release --target ztmz_dashboard_overlay --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Native dashboard compilation failed.' }
    $dll = Join-Path $BuildDirectory 'Release/ztmz_dashboard_overlay.dll'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll)
    if ($version.FileVersion -ne '1.0.0.0' -or $version.ProductName -ne 'ZTMZ Native Dashboard') {
        throw 'The native build did not produce the expected production overlay DLL.'
    }
} finally {
    if ($held) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
