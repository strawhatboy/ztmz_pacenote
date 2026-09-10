$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output = Join-Path $PSScriptRoot 'mod-uploads'
[void][IO.Directory]::CreateDirectory($output)
$lights = 'L:\Richard Burns Rally\MODS 插件集合包\5 灯光插件RBRGAL LIGHTS V3 by Black-f&Z3tta（page up-down是打开关闭-小键盘7,4调远近灯）'

function HashStream($stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function BuildPackage($name, $sources) {
    $path = Join-Path $output $name
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    $hashes = @{}
    try {
        foreach ($source in $sources) {
            $entryName = $source.Name.Replace('\', '/')
            if ($entryName.StartsWith('/') -or $entryName.Contains(':') -or
                $entryName.Split('/') -contains '..' -or $hashes.ContainsKey($entryName)) {
                throw "Invalid or duplicate archive path: $entryName"
            }
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $inputStream = & $source.Open
            $target = $entry.Open()
            try { $inputStream.CopyTo($target) }
            finally { $inputStream.Dispose(); $target.Dispose() }
            $hashes[$entryName] = HashStream (& $source.Open)
        }
    }
    finally { $archive.Dispose() }
    $check = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        if ($check.Entries.Count -ne $hashes.Count) { throw 'Entry count mismatch' }
        foreach ($entry in $check.Entries) {
            if ((HashStream $entry.Open()) -ne $hashes[$entry.FullName]) {
                throw "Content mismatch: $($entry.FullName)"
            }
        }
    }
    finally { $check.Dispose() }
    [pscustomobject]@{ File = $path; Files = $hashes.Count; Bytes = (Get-Item $path).Length; SHA256 = (Get-FileHash $path).Hash }
}

$sources = foreach ($file in Get-ChildItem -LiteralPath $lights -Recurse -File | Sort-Object FullName) {
    if ($file.Extension -eq '.log') { continue }
    $sourcePath = $file.FullName
    @{ Name = $sourcePath.Substring($lights.Length + 1); Open = { [IO.File]::OpenRead($sourcePath) }.GetNewClosure() }
}
BuildPackage 'RBRGAL-LIGHTS-v3.zip' $sources

$original = [IO.Compression.ZipFile]::OpenRead('C:\Users\Andy\Downloads\SimRallyCN-v4.1.1.0 (2).zip')
try {
    $sources = foreach ($entry in $original.Entries) {
        if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) { continue }
        $sourceEntry = $entry
        @{ Name = $entry.FullName; Open = { $sourceEntry.Open() }.GetNewClosure() }
    }
    BuildPackage 'SimRallyCN-v4.1.1.0.zip' $sources
}
finally { $original.Dispose() }
