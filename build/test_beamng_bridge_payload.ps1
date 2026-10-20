param([Parameter(Mandatory = $true)][string]$ValidModRoot)

$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'verify_beamng_bridge_payload.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$scratch = Join-Path $tempRoot ('ztmz-beamng-payload-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch
try {
    $payload = Join-Path $scratch 'BeamNGMod'
    Copy-Item -LiteralPath $ValidModRoot -Destination $payload -Recurse
    function Expect-Rejection([string]$ExpectedMessage) {
        $failure = $null
        try { & $validator -ModRoot $payload } catch { $failure = $_.Exception.Message }
        if (-not $failure -or $failure -notlike $ExpectedMessage) {
            throw "Expected '$ExpectedMessage', got '$failure'."
        }
        Write-Host "PASS: $ExpectedMessage"
    }
    & $validator -ModRoot $payload
    $metadataPath = Join-Path $payload 'mod_info/ZTMZPACENOTERALLYBRIDGE/info.json'
    $metadataBytes = [IO.File]::ReadAllBytes($metadataPath)
    $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $metadata.version_string = '1.1.11'
    $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding UTF8
    Expect-Rejection 'Stale bundled BeamNG bridge file:*info.json'
    [IO.File]::WriteAllBytes($metadataPath, $metadataBytes)

    $lua = Join-Path $payload 'scripts/ztmz_pacenote_rally_bridge/extension.lua'
    $luaBytes = [IO.File]::ReadAllBytes($lua)
    [IO.File]::AppendAllText($lua, "`n-- stale code despite current version metadata")
    Expect-Rejection 'Stale bundled BeamNG bridge file:*extension.lua'
    [IO.File]::WriteAllBytes($lua, $luaBytes)
    Remove-Item -LiteralPath $lua
    Expect-Rejection 'Missing bundled BeamNG bridge file:*extension.lua'
    [IO.File]::WriteAllBytes($lua, $luaBytes)

    $legacy = Join-Path $payload 'legacy-input-actions.json'
    [IO.File]::WriteAllText($legacy, '{}')
    Expect-Rejection 'Unexpected bundled BeamNG bridge file:*legacy-input-actions.json'
    Remove-Item -LiteralPath $legacy
    & $validator -ModRoot $payload
    Write-Host 'PASS: current and restored payloads accepted; old metadata, stale Lua, missing and extra files rejected.'
} finally {
    if ((Split-Path -Parent ([IO.Path]::GetFullPath($scratch))) -ne $tempRoot) {
        throw 'Refusing cleanup outside the temporary directory.'
    }
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
