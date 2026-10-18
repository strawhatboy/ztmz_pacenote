param(
    [Parameter(Mandatory = $true)][string]$DestinationRoot,
    [switch]$VerifyOnly,
    [string]$CacheRoot
)

$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $CacheRoot) { $CacheRoot = Join-Path $scriptDirectory 'resource-cache' }
. (Join-Path $scriptDirectory 'release_resource_helpers.ps1')
$manifest = Get-Content -LiteralPath (Join-Path $scriptDirectory 'release-resources.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw 'Unsupported release resource manifest version.' }
$DestinationRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationRoot)
$audioDirectory = $manifest.audio.directory
if ([string]::IsNullOrWhiteSpace($audioDirectory) -or $audioDirectory -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*$') {
    throw 'Invalid audio package directory in release resource manifest.'
}
$audioTarget = Join-Path (Join-Path $DestinationRoot 'codrivers') $audioDirectory
$profilesTarget = Join-Path $DestinationRoot 'profiles/default'

function Assert-OnlyChildren([string]$Root, [string[]]$Allowed) {
    foreach ($entry in Get-ChildItem -LiteralPath $Root -Force) {
        if (-not $entry.PSIsContainer -or $entry.Name -cnotin $Allowed -or
            ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Unexpected release resource entry: $($entry.FullName)"
        }
    }
}

function Assert-StagedResources {
    Assert-OnlyChildren (Join-Path $DestinationRoot 'codrivers') @($audioDirectory)
    Assert-OnlyChildren (Join-Path $DestinationRoot 'profiles') @('default')
    Assert-OnlyChildren $profilesTarget @($manifest.profiles.games.name)
    Assert-ReleaseTree $audioTarget $manifest.audio
    $audioInfo = Get-Content -LiteralPath (Join-Path $audioTarget 'info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($audioInfo.id -ne $manifest.audio.packageId) { throw 'Bundled audio package identity does not match the release manifest.' }
    foreach ($game in $manifest.profiles.games) {
        Assert-ReleaseTree (Join-Path $profilesTarget $game.name) $game
    }
}

if ($VerifyOnly) {
    Assert-StagedResources
    exit 0
}

# Refuse to merge with leftovers. publish.bat creates a fresh target tree.
foreach ($folder in @('codrivers', 'profiles')) {
    $path = Join-Path $DestinationRoot $folder
    if ((Test-Path -LiteralPath $path) -and @(Get-ChildItem -LiteralPath $path -Force).Count -gt 0) {
        throw "Resource destination is not empty: $path. Run publish into a clean staging directory."
    }
}
$sevenZipCommand = Get-Command 7z.exe -ErrorAction SilentlyContinue
$sevenZip = if ($sevenZipCommand) { $sevenZipCommand.Source } else { Join-Path $env:ProgramFiles '7-Zip/7z.exe' }
if (-not (Test-Path -LiteralPath $sevenZip)) { throw '7-Zip is required to unpack default.zpak.' }
$null = Get-Command git.exe -ErrorAction Stop
$null = Get-Command curl.exe -ErrorAction Stop
$CacheRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CacheRoot)
$null = New-Item -ItemType Directory -Path $CacheRoot -Force
$work = Join-Path $CacheRoot ([guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work
try {
    $audioArchive = Join-Path $CacheRoot ($manifest.audio.archiveSha256 + '.zpak')
    if (-not (Test-Path -LiteralPath $audioArchive)) {
        $download = Join-Path $work 'default.zpak'
        & curl.exe --fail --location --silent --show-error --retry 2 --connect-timeout 30 --max-time 300 --output $download $manifest.audio.url
        if ($LASTEXITCODE -ne 0) { throw 'Default audio download failed; no local data fallback is allowed.' }
        if ((Get-ReleaseSha256 $download) -ne $manifest.audio.archiveSha256) { throw 'Downloaded default audio SHA256 does not match the pinned release.' }
        Copy-Item -LiteralPath $download -Destination $audioArchive -Force
    }
    if ((Get-ReleaseSha256 $audioArchive) -ne $manifest.audio.archiveSha256) { throw "Corrupt audio cache: $audioArchive" }
    $audioWork = Join-Path $work 'audio'
    & $sevenZip x $audioArchive "-o$audioWork" -y -bso0
    if ($LASTEXITCODE -ne 0) { throw 'Default audio extraction failed.' }
    Assert-OnlyChildren $audioWork @($audioDirectory)
    Assert-ReleaseTree (Join-Path $audioWork $audioDirectory) $manifest.audio

    $repo = Join-Path $CacheRoot 'pacenote-scripts.git'
    if (-not (Test-Path -LiteralPath $repo)) {
        & git.exe init --bare --quiet $repo
        if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize the release resource cache.' }
    }
    # Export immutable git blobs; do not use a mutable checkout or core.autocrlf.
    $null = & git.exe -C $repo rev-parse --verify --quiet ($manifest.profiles.commit + '^{commit}')
    if ($LASTEXITCODE -ne 0) {
        & git.exe -C $repo -c core.sshCommand='ssh -o BatchMode=yes -o ConnectTimeout=30' fetch --depth=1 $manifest.profiles.repository $manifest.profiles.commit
        if ($LASTEXITCODE -ne 0) { throw 'Pinned pacenote commit download failed; no local data fallback is allowed.' }
    }
    $profileArchive = Join-Path $work 'profiles.zip'
    $paths = @($manifest.profiles.games | ForEach-Object { 'default/' + $_.name })
    & git.exe -C $repo archive --format=zip "--output=$profileArchive" $manifest.profiles.commit -- @paths
    if ($LASTEXITCODE -ne 0) { throw 'Cannot export the pinned pacenote commit.' }
    $profileWork = Join-Path $work 'profiles'
    & $sevenZip x $profileArchive "-o$profileWork" -y -bso0
    if ($LASTEXITCODE -ne 0) { throw 'Pacenote extraction failed.' }
    foreach ($game in $manifest.profiles.games) {
        Assert-ReleaseTree (Join-Path (Join-Path $profileWork 'default') $game.name) $game
    }

    $null = New-Item -ItemType Directory -Path (Join-Path $DestinationRoot 'codrivers') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $DestinationRoot 'profiles') -Force
    Copy-Item -LiteralPath (Join-Path $audioWork $audioDirectory) -Destination (Join-Path $DestinationRoot 'codrivers') -Recurse
    Copy-Item -LiteralPath (Join-Path $profileWork 'default') -Destination (Join-Path $DestinationRoot 'profiles') -Recurse
    Assert-StagedResources
} finally {
    # Only the unique scratch directory created by this invocation is removed.
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
