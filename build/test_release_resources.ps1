param([Parameter(Mandatory = $true)][string]$ValidStagingRoot)
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$validator = Join-Path $scriptDirectory 'stage_release_resources.ps1'
$manifest = Get-Content -LiteralPath (Join-Path $scriptDirectory 'release-resources.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$installer = Get-Content -LiteralPath (Join-Path $scriptDirectory 'setup.iss') -Raw -Encoding UTF8
if ($installer.Contains('Name: "{code:GetZTMZHome}\codrivers\default";')) {
    throw 'The Serena installer must retain the legacy Xiaoyi directory.'
}
if (-not $installer.Contains('Name: "{code:GetZTMZHome}\codrivers\' + $manifest.audio.directory + '";')) {
    throw 'The installer refresh directory must match the bundled audio directory.'
}
Write-Host 'PASS: Installer refreshes Serena and retains legacy Xiaoyi'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ztmz-resource-tests-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch
try {
    $staging = Join-Path $scratch 'staging'
    Copy-Item -LiteralPath $ValidStagingRoot -Destination $staging -Recurse
    function Check([string]$Name, [bool]$ExpectedSuccess) {
        $log = Join-Path $scratch 'validation.log'
        # Windows PowerShell converts native stderr into error records. Expected
        # validator failures must be judged by exit code, not abort this harness.
        $previousPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -VerifyOnly -DestinationRoot $staging > $log 2>&1
            $passed = $LASTEXITCODE -eq 0
        } finally { $ErrorActionPreference = $previousPreference }
        if ($passed -ne $ExpectedSuccess) { throw "$Name failed: $(Get-Content -LiteralPath $log -Raw)" }
        Write-Host "PASS: $Name"
    }
    Check 'Valid online resources' $true
    $dr1 = Join-Path $staging 'profiles/default/Dirt Rally 1.0'
    $file = Get-ChildItem -LiteralPath $dr1 -File | Select-Object -First 1
    $saved = Join-Path $scratch 'saved.pacenote'
    Move-Item -LiteralPath $file.FullName -Destination $saved
    Check 'Missing DR1 file rejected' $false
    Move-Item -LiteralPath $saved -Destination $file.FullName
    $original = [IO.File]::ReadAllBytes($file.FullName)
    try {
        [IO.File]::AppendAllText($file.FullName, "`n# unexpected modification")
        Check 'Modified file rejected' $false
    } finally { [IO.File]::WriteAllBytes($file.FullName, $original) }
    $extra = Join-Path $dr1 'unexpected.pacenote'
    Copy-Item -LiteralPath $file.FullName -Destination $extra
    Check 'Extra file rejected' $false
    Remove-Item -LiteralPath $extra
    $acr = Join-Path $staging 'profiles/default/Assetto Corsa Rally'
    $null = New-Item -ItemType Directory -Path $acr
    Check 'Even an empty ACR profile directory rejected' $false
    Remove-Item -LiteralPath $acr
    $audio = Get-ChildItem -LiteralPath (Join-Path $staging 'codrivers') -Recurse -File -Filter '*.ogg' | Select-Object -First 1
    Move-Item -LiteralPath $audio.FullName -Destination (Join-Path $scratch 'saved.ogg')
    Check 'Missing audio rejected' $false
    Move-Item -LiteralPath (Join-Path $scratch 'saved.ogg') -Destination $audio.FullName
    Check 'Restored resources accepted' $true
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
