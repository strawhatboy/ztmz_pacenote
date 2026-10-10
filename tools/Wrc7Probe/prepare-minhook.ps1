$ErrorActionPreference = 'Stop'
# Generate only the WRC7 engine-hook variant; never edit the shared submodule.
$source = Join-Path $PSScriptRoot '../ACRPacenoteTrace/third_party/minhook/src/buffer.c'
$content = [IO.File]::ReadAllText($source)
$expected = '#define MAX_MEMORY_RANGE 0x40000000'
if ([regex]::Matches($content, [regex]::Escape($expected)).Count -ne 1) {
    throw 'MinHook allocator changed; review the WRC7-specific range patch before building.'
}
$generated = $content.Replace($expected, '#define MAX_MEMORY_RANGE 0x60000000')
$output = Join-Path $PSScriptRoot 'bin/wrc7-buffer.c'
[IO.File]::WriteAllText($output, $generated, [Text.UTF8Encoding]::new($false))
