param([Parameter(Mandatory = $true)][string]$BuildDirectory,
    [switch]$RunTests,
    [string]$CMakePath = $env:ZTMZ_CMAKE_PATH)
$ErrorActionPreference = 'Stop'
try {
$source = Join-Path $PSScriptRoot '../src/ZTMZ.PacenoteTool.Launcher'
$BuildDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($BuildDirectory)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio 2022 C++ tools and CMake are required to build the startup launcher.' }
$installation = & $vswhere -latest -products '*' -version '[17.0,18.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'Install Visual Studio 2022 Desktop development with C++ (including Windows SDK and CMake).' }
$cmake = $CMakePath
if ($cmake) {
    if (!(Test-Path -LiteralPath $cmake -PathType Leaf)) { throw "CMake executable does not exist: $cmake" }
} else {
    $onPath = Get-Command cmake.exe -CommandType Application -ErrorAction SilentlyContinue
    $candidates = @(
        (Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe')
        $(if ($onPath) { $onPath.Source })
        (Join-Path $env:ProgramFiles 'CMake/bin/cmake.exe')
        (Join-Path $PSScriptRoot 'native-dashboard/tools/cmake/bin/cmake.exe')
    )
    $cmake = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
    if (!$cmake) {
        throw 'CMake 3.24+ was not found. Install Visual Studio C++ CMake tools, add CMake to PATH, or set ZTMZ_CMAKE_PATH to cmake.exe. A portable copy can also be placed at build/native-dashboard/tools/cmake/bin/cmake.exe.'
    }
}
$cmakeVersion = & $cmake --version
if ($LASTEXITCODE -ne 0 -or ($cmakeVersion -join ' ') -notmatch 'cmake version (\d+\.\d+\.\d+)') {
    throw "Cannot read CMake version from $cmake."
}
if ([version]$Matches[1] -lt [version]'3.24.0') { throw "CMake 3.24+ is required; found $($Matches[1]) at $cmake." }
Write-Host "Using CMake: $cmake"
# Direct builds and release publishing share this tree; serialize CMake builds.
$sha = [Security.Cryptography.SHA256]::Create()
try { $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($BuildDirectory.ToLowerInvariant()))).Replace('-', '') }
finally { $sha.Dispose() }
$mutex = New-Object Threading.Mutex($false, "Local\ZtmzStartupLauncher-$key")
$held = $false
try {
    try { $held = $mutex.WaitOne([TimeSpan]::FromMinutes(10)) }
    catch [Threading.AbandonedMutexException] { $held = $true }
    if (!$held) { throw 'Timed out waiting for the other startup launcher build.' }
    $testFlag = if ($RunTests) { 'ON' } else { 'OFF' }
    & $cmake -S $source -B $BuildDirectory -G 'Visual Studio 17 2022' -A x64 "-DCMAKE_GENERATOR_INSTANCE=$installation" "-DBUILD_TESTING=$testFlag" '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded'
    if ($LASTEXITCODE -ne 0) { throw 'Startup launcher CMake configuration failed.' }
    & $cmake --build $BuildDirectory --config Release --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Startup launcher compilation failed.' }
    if ($RunTests) {
        & (Join-Path (Split-Path $cmake) 'ctest.exe') --test-dir $BuildDirectory -C Release --output-on-failure
        if ($LASTEXITCODE -ne 0) { throw 'Startup launcher tests failed.' }
    }
    if (!(Test-Path (Join-Path $BuildDirectory 'Release/ZTMZ.Launcher.exe'))) { throw 'Startup launcher executable missing.' }
} finally {
    if ($held) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
} catch {
    # MSBuild's terminal logger otherwise often shows only MSB3073 / exit code 1.
    Write-Host "error : Startup launcher build failed: $($_.Exception.Message)"
    exit 1
}
