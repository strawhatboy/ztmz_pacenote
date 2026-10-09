param(
    [Parameter(Mandatory)][string]$DefinitionsDirectory,
    [string]$TracePath = "$PSScriptRoot/bin/WrcGenerationsTrace.log"
)
$ErrorActionPreference = 'Stop'
# Parse only the simple data tables, never execute game configuration as code.
function Read-Table([string]$Path, [string]$Name) {
    $source = [IO.File]::ReadAllText($Path)
    $match = [regex]::Match($source, '(?ms)^' + [regex]::Escape($Name) + '\s*=\s*\{(.*?)^\}')
    if (!$match.Success) { throw "Missing table $Name in $Path" }
    return $match.Groups[1].Value
}
$atoms = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$sounds = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$body = Read-Table "$DefinitionsDirectory/CHUNK_0_PART0000-PACENOTES.CFG" 'PaceNotes'
foreach ($entry in [regex]::Matches($body, '(?m)^\s*(\w+)\s*=\s*\{([^{}]*)\}')) {
    $name = $entry.Groups[1].Value
    $fields = $entry.Groups[2].Value
    $atoms.Add($name, [regex]::Match($fields, '\bText\s*=\s*"([^"]*)"').Groups[1].Value)
    foreach ($sound in [regex]::Matches($fields, '\b(?:DetailedSound|AccessibleSound)\s*=\s*"([^"]+)"')) {
        $key = $sound.Groups[1].Value
        if ($sounds.ContainsKey($key) -and $sounds[$key] -cne $name) { throw "Ambiguous sound: $key" }
        $sounds[$key] = $name
    }
}
$blocks = [Collections.Generic.Dictionary[string,string[]]]::new([StringComparer]::Ordinal)
$used = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$body = Read-Table "$DefinitionsDirectory/CHUNK_0_PART0000-PACENOTEBLOCKS.CFG" 'PaceNoteBlocks'
foreach ($entry in [regex]::Matches($body, '(Block_\d+)\s*=\s*\{([^{}]*)\}')) {
    [string[]]$tokens = @([regex]::Matches($entry.Groups[2].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    foreach ($token in $tokens) {
        if (!$atoms.ContainsKey($token)) { throw "Undefined atom $token in $($entry.Groups[1].Value)" }
        [void]$used.Add($token)
    }
    $blocks.Add($entry.Groups[1].Value, $tokens)
}
if (!$atoms.Count -or !$blocks.Count) { throw 'No definitions parsed' }
Write-Output "Validated $($blocks.Count) blocks, $($used.Count) used atoms, $($atoms.Count) atom definitions."
$stream = [IO.File]::Open((Resolve-Path $TracePath).Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
$reader = [IO.StreamReader]::new($stream)
try { $snapshot = $reader.ReadToEnd() } finally { $reader.Dispose() }
foreach ($line in ($snapshot -split '\r?\n')) {
    if ($line -notmatch '\bsound-info\s+\S+\s+0\s+\S+\s+(\S+)') { continue }
    $resource = $Matches[1]
    $name = ($resource -split '/')[-1]
    if ($blocks.ContainsKey($name)) { $tokens = $blocks[$name] }
    elseif ($sounds.ContainsKey($name)) { $tokens = @($sounds[$name]) }
    else { Write-Output "$resource => UNMAPPED (not assumed to be a pacenote)"; continue }
    Write-Output "$resource => $($tokens -join ' / ')"
}
