param([Parameter(Mandatory = $true)][string]$DestinationRoot)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release_resource_helpers.ps1')
$root = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationRoot)
function Require-File([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Release payload missing or empty: $RelativePath"
    }
    return $path
}

$required = @(
    'ZTMZ.Launcher.exe', 'ZTMZ.startup.json', 'ThirdPartyNotices/Launcher-nlohmann-json.txt',
    'ZTMZ.PacenoteTool.WpfGUI.exe', 'ZTMZ.PacenoteTool.WpfGUI.dll',
    'ZTMZ.PacenoteTool.WpfGUI.deps.json', 'ZTMZ.PacenoteTool.WpfGUI.runtimeconfig.json',
    'ZTMZ.PacenoteTool.Base.dll', 'ZTMZ.PacenoteTool.Core.dll',
    'ZTMZ.PacenoteTool.WebDashboard.dll', 'ZTMZ.PacenoteTool.ScriptEditor.dll',
    'games/ZTMZ.PacenoteTool.Codemasters.dll', 'games/ZTMZ.PacenoteTool.RBR.dll',
    'games/ZTMZ.PacenoteTool.BeamNG.dll', 'games/ZTMZ.PacenoteTool.AC.dll',
    'games/ZTMZ.PacenoteTool.KT.dll', 'Assets/Games/wrcg.jpg',
    'games/WRCGBridge/WrcGenerationsBridge.dll', 'games/WRCGBridge/WrcGenerationsBridgeInject.exe',
    'Assets/Games/wrc9.jpg', 'games/WRC9Bridge/Wrc9Bridge.dll', 'games/WRC9Bridge/Wrc9BridgeInject.exe', 'games/WRC9Bridge/LICENSE.txt',
    'Assets/Games/wrc7.jpg', 'games/WRC7Bridge/Wrc7BridgeAbsolute.dll', 'games/WRC7Bridge/Wrc7BridgeAbsoluteInject.exe',
    'games/WRCGBridge/LICENSE.txt', 'games/WRC7Bridge/LICENSE.txt',
    'games/ACRExperimentalBridge/ACRFmodLivePacenoteBridge.dll',
    'games/ACRExperimentalBridge/ACRPacenoteTraceInjector.exe',
    'games/ACRExperimentalBridge/ACRProtonRelay.exe',
    'games/ACRExperimentalBridge/acr-live-bridge.manifest.json',
    'fonts/WRCCleanRoman.ttf', 'fonts/WRCCleanBold.ttf',
    'WebView2Loader.dll', 'ThirdPartyNotices/Aim_2_MoTeC.txt',
    'native-dashboard/win-x64/ztmz_dashboard_overlay.dll',
    'native-dashboard/win-x86/ztmz_dashboard_overlay.dll',
    'native-dashboard/win-x86/ztmz_dashboard_injector.exe',
    'native-dashboard/win-x64/licenses/volk.txt', 'native-dashboard/win-x86/licenses/volk.txt',
    'Assets/Monaco/index.html', 'Assets/Monaco/min/vs/loader.js',
    'Assets/Monaco/min/vs/editor/editor.main.js',
    'wwwroot/index.html', 'wwwroot/dashboard-client.js', 'wwwroot/browser-lua-dashboard.js',
    'wwwroot/vendor/fengari-web.js', 'wwwroot/screen-awake.js',
    'wwwroot/vendor/nosleep-media.js', 'wwwroot/vendor/nosleep-LICENSE.txt'
)
foreach ($relative in $required) { $null = Require-File $relative }

# The launcher must be a native x64 executable, usable before .NET loads.
$launcherStream = [IO.File]::OpenRead((Join-Path $root 'ZTMZ.Launcher.exe'))
$launcherReader = New-Object IO.BinaryReader($launcherStream)
try {
    $launcherStream.Position = 0x3c
    $peOffset = $launcherReader.ReadInt32()
    $launcherStream.Position = $peOffset
    if ($launcherReader.ReadUInt32() -ne 0x4550 -or $launcherReader.ReadUInt16() -ne 0x8664) { throw 'Startup launcher is not an x64 PE.' }
    $launcherStream.Position = $peOffset + 24
    if ($launcherReader.ReadUInt16() -ne 0x20b) { throw 'Startup launcher is not PE32+.' }
    $launcherStream.Position = $peOffset + 24 + 112 + (14 * 8)
    if ($launcherReader.ReadUInt64() -ne 0) { throw 'Startup launcher depends on the CLR.' }
} finally { $launcherReader.Dispose(); $launcherStream.Dispose() }
$startupManifest = Get-Content -LiteralPath (Join-Path $root 'ZTMZ.startup.json') -Raw | ConvertFrom-Json
if ($startupManifest.schemaVersion -ne 1) { throw 'Invalid startup manifest schema.' }
foreach ($relative in $startupManifest.applicationFiles) {
    if ([IO.Path]::IsPathRooted($relative) -or ($relative -split '[/\\]') -contains '..' -or $relative -like '*.resources.dll') { throw "Invalid startup requirement: $relative" }
    $null = Require-File $relative
}

# RID-specific publish flattens runtime/native dependencies into the host folder.
$deps = Get-Content -LiteralPath (Join-Path $root 'ZTMZ.PacenoteTool.WpfGUI.deps.json') -Raw | ConvertFrom-Json
$target = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value
foreach ($library in $target.PSObject.Properties) {
    foreach ($kind in @('runtime', 'native')) {
        if ($null -eq $library.Value.$kind) { continue }
        foreach ($asset in $library.Value.$kind.PSObject.Properties) {
            if ($asset.Name.EndsWith('/_._')) { continue }
            $requiredAsset = [IO.Path]::GetFileName($asset.Name)
            $null = Require-File $requiredAsset
            if ($startupManifest.applicationFiles -notcontains $requiredAsset) { throw "Startup manifest omits dependency: $requiredAsset" }
        }
    }
    # Third-party localized resource assemblies are intentionally not shipped.
}

# Include every provider's shipped language file, not just the two KT languages.
$repo = Split-Path -Parent $PSScriptRoot
# Verify all bundled dashboard scripts/settings/translations and browser assets against source.
# Presence alone cannot detect stale Lua/JS files left by an incomplete publish.
$webSource = Join-Path $repo 'src/ZTMZ.PacenoteTool.WebDashboard'
foreach ($folder in @('dashboards', 'wwwroot', 'fonts')) {
    $source = Join-Path $webSource $folder
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
        $relative = $folder + '/' + $file.FullName.Substring($source.Length + 1)
        $published = Require-File $relative
        if ((Get-ReleaseSha256 $published) -ne (Get-ReleaseSha256 $file.FullName)) {
            throw "Stale published dashboard asset: $relative"
        }
    }
}
# Reject old Lua as well as old metadata, including leftover legacy mod files.
& (Join-Path $PSScriptRoot 'verify_beamng_bridge_payload.ps1') -ModRoot (Join-Path $root 'games/BeamNGMod')
# These databases are installed directly from source by setup.iss.
foreach ($database in @('src/ZTMZ.PacenoteTool.Base/data.zdb', 'src/ZTMZ.PacenoteTool.RBR/default.zdb', 'src/ZTMZ.PacenoteTool.RBR/jannemod_v3.zdb')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo $database) -PathType Leaf)) { throw "Missing installer database: $database" }
}
foreach ($provider in @('Codemasters', 'RBR', 'BeamNG', 'AC', 'KT')) {
    foreach ($file in Get-ChildItem (Join-Path $repo "src/ZTMZ.PacenoteTool.$provider/lang") -Filter '*.json' -File) {
        $published = Require-File ('games/lang/' + $file.Name)
        if ((Get-ReleaseSha256 $published) -ne (Get-ReleaseSha256 $file.FullName)) {
            throw "Stale published provider localization: $($file.Name)"
        }
    }
}
foreach ($file in Get-ChildItem (Join-Path $repo 'src/ZTMZ.PacenoteTool.I18N/lang') -Filter '*.json' -File) {
    $published = Require-File ('lang/' + $file.Name)
    if ((Get-ReleaseSha256 $published) -ne (Get-ReleaseSha256 $file.FullName)) {
        throw "Stale published localization: $($file.Name)"
    }
}
foreach ($file in Get-ChildItem (Join-Path $repo 'src/ZTMZ.PacenoteTool.WpfGUI') -Filter '*beta*.txt' -File) {
    $published = Require-File $file.Name
    if ((Get-ReleaseSha256 $published) -ne (Get-ReleaseSha256 $file.FullName)) {
        throw "Stale published release notes: $($file.Name)"
    }
}

$manifestPath = Require-File 'Assets/PacenoteIcons/v1/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($icon in $manifest.icons.PSObject.Properties) {
    $path = Require-File ('Assets/PacenoteIcons/v1/' + $icon.Value.file)
    if ((Get-ReleaseSha256 $path) -ne $icon.Value.sha256) {
        throw "Corrupt published pacenote icon: $($icon.Name)"
    }
}

# Catch an x64 artifact accidentally copied into the RBR/x86 payload (or vice versa).
foreach ($relative in $required | Where-Object { $_ -match '(WRCGBridge|WRC7Bridge|WRC9Bridge|ACRExperimentalBridge|native-dashboard)/.*\.(dll|exe)$' }) {
    $stream = [IO.File]::OpenRead((Join-Path $root $relative))
    $reader = New-Object IO.BinaryReader($stream)
    try {
        $stream.Position = 0x3c
        $pe = $reader.ReadInt32()
        $stream.Position = $pe
        if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid native PE: $relative" }
        $machine = $reader.ReadUInt16()
        $expected = if ($relative -like 'native-dashboard/win-x86/*') { 0x14c } else { 0x8664 }
        if ($machine -ne $expected) { throw "Wrong native architecture: $relative" }
    } finally { $reader.Dispose() }
}
Write-Host "Release payload verified: providers, bridges, native architectures, languages, editor, web client, icons, fonts and release notes."
