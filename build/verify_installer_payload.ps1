param(
    [Parameter(Mandatory = $true)][string]$ExtractedRoot,
    [string]$PublishedRoot
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release_resource_helpers.ps1')
if (-not $PublishedRoot) {
    $PublishedRoot = Join-Path $PSScriptRoot 'target/publish/ZTMZ.PacenoteTool.WpfGUI/release_win-x64'
}
$published = (Resolve-Path -LiteralPath $PublishedRoot).Path
$extracted = (Resolve-Path -LiteralPath $ExtractedRoot).Path
$repo = Split-Path -Parent $PSScriptRoot
$install = Join-Path $extracted '{code_GetInstallDir}'
$data = Join-Path $extracted '{code_GetZTMZHome}'
$failures = New-Object 'System.Collections.Generic.List[string]'
$script:checked = 0

# Preserve the intentional exclusion of third-party translations. ZTMZ's own
# language JSON files are checked below and remain part of the installer.
foreach ($file in Get-ChildItem -LiteralPath $install -Recurse -Filter '*.resources.dll' -File) {
    $failures.Add("Unexpected third-party language resource: $($file.FullName)")
}

function Compare-InstalledFile([string]$Source, [string]$Root, [string]$Relative) {
    $destination = Join-Path $Root $Relative
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        $failures.Add("Missing: $destination")
    } elseif ((Get-ReleaseSha256 $Source) -ne (Get-ReleaseSha256 $destination)) {
        $failures.Add("Different contents: $destination")
    }
    $script:checked++
}

# Check the compiled installer, not merely the publish tree. Extract with
# innounp -x -a -b -d<empty-directory> <installer.exe>; -a retains both font copies.
# Every published file needs an explicit destination or a documented exclusion.
foreach ($file in Get-ChildItem -LiteralPath $published -Recurse -File) {
    $relative = $file.FullName.Substring($published.Length + 1).Replace('\', '/')
    $folder = $relative.Split('/')[0]
    if ($relative -like '*.resources.dll') { continue }
    if (-not $relative.Contains('/')) {
        # Legacy root exports are not application runtime assets.
        if ($relative -like '*.csv' -or $relative -like 'XAudio*') { continue }
        Compare-InstalledFile $file.FullName $install $relative
    } elseif ($folder -in @('codrivers', 'profiles', 'lang', 'dashboards')) {
        Compare-InstalledFile $file.FullName $data $relative
    } elseif ($folder -eq 'fonts') {
        Compare-InstalledFile $file.FullName $install $relative
        Compare-InstalledFile $file.FullName $data $relative
    } elseif ($folder -eq 'games') {
        # Providers load through the host dependency graph, not their own deps files.
        if ($relative -like '*.deps.json') { continue }
        if ($file.Extension -eq '.json') {
            Compare-InstalledFile $file.FullName $data $relative
            if ($relative -match '^games/(lang|BeamNGMod|ACRExperimentalBridge)/') {
                Compare-InstalledFile $file.FullName $install $relative
            }
        } else {
            Compare-InstalledFile $file.FullName $install $relative
        }
    } elseif ($folder -eq 'ACRExperimentalBridge') {
        # Project-reference output duplicates the canonical games/ bridge bundle.
        Compare-InstalledFile $file.FullName $install ('games/' + $relative)
    } elseif ($relative -eq 'runtimes/win-x64/native/WebView2Loader.dll') {
        # The RID-specific publish also provides this identical loader at the root.
        Compare-InstalledFile $file.FullName $install 'WebView2Loader.dll'
    } elseif ($folder -in @('Assets', 'wwwroot', 'native-dashboard', 'x64', 'x86', 'ThirdPartyNotices')) {
        Compare-InstalledFile $file.FullName $install $relative
    } else {
        $failures.Add("Unclassified published file (review installer destination): $relative")
    }
}

# These databases are packaged directly from source, outside the publish tree.
foreach ($file in Get-ChildItem (Join-Path $repo 'src/ZTMZ.PacenoteTool.Base') -Filter '*.zdb' -File) {
    Compare-InstalledFile $file.FullName $data $file.Name
}
foreach ($file in Get-ChildItem (Join-Path $repo 'src/ZTMZ.PacenoteTool.RBR') -Filter '*.zdb' -File) {
    Compare-InstalledFile $file.FullName $data ('games/' + $file.Name)
}
if ($failures.Count -gt 0) {
    throw "Installer payload failed ($($failures.Count) issues):`n$($failures -join "`n")"
}
Write-Host "Installer payload verified: $checked destination/content comparisons; all published files accounted for."
