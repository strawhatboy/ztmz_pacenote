# Native dashboard font memory in 32-bit games

## RBR crash evidence (2026-10-17)

Enabling WRC 2025 crashed RBR after the overlay had already rendered thousands
of DX9 frames. The 20:04:44 game dump identifies a write access violation in
the native overlay's CRT memcpy, with destination EDI=0. This is separate from
the earlier GaugerPlugin probe-device crash.

The loaded x86 overlay SHA256 was
`AE9AE595CE1FFE1C662393147AED6C0520C6ED04C326E3D8F51D95423A99AD27`.
At module RVA `0xA712A`, ImGui's AddFont copies to the pointer returned by its
allocator without checking for null. The call originated from
AddFontFromMemoryTTF during the background atlas build; the font data size was
`0xD31EC8` (about 13.2 MiB), at 27 pixels. The faulting memcpy instruction was
at RVA `0xC9703`. The dump and matching DLL are preserved locally under
`tmp/rbr-wrc2025-font-crash/` and are not repository artifacts.

The pinned ImGui 1.91.9b does **not** borrow an input font when
`FontDataOwnedByAtlas=false`: AddFont allocates and owns a copy. Previously,
every cached size added every fallback face, copying large system font files
again. Rebuilding also temporarily keeps the previous atlas alive. This can
exhaust a 32-bit game's address space; a null allocation then becomes a fatal
access violation instead of the C++ exception caught by the font worker.

## Fix and ownership

Register cached immutable bytes with copying disabled by setting input ownership
to true, then immediately mark the stored source as non-owning. FontSelection
and the catalog retain the font buffers; atlases neither copy nor free them.
The pinned AddFont path performs no allocation after registering an owned source.
A version assertion requires this integration to be checked when ImGui changes.

Only add fallback faces that supply still-missing requested/default-range glyphs.
Keep Latin-1 coverage, CJK fallback, selected font styles and cached pixel sizes.
Once the worker has copied the texture into ImageAsset for GPU upload/recovery,
release ImGui's redundant alpha/RGBA CPU bitmaps; glyphs and UVs remain valid.

## Validation

- The new font-source regression fails against the previous implementation:
  `Font atlas duplicated a cached font file instead of sharing it`.
- x86 and x64 Release builds pass `font_atlas_memory`, `lua_runtime`,
  `builtin_dashboard_lua` and `backend_contract`.
- Tests cover cached source ownership, redundant fallback omission, Chinese
  glyphs, resizing, simultaneous old/new atlases and destruction/rebuilding.
- Both WRC 2025 scripts now also run through the production asynchronous font
  worker and font-aware drawing at two viewport scales. The test checks that
  upload pixels remain available while redundant ImGui bitmaps are released.

These checks do not simulate every cause of process memory exhaustion or replace
a live test inside a heavily modded game. Live RBR reactivation is pending.

## Map Information text clipping

A subsequent report described visible labels but missing/cut-off values in the
right-aligned Map Information dashboard. Its seven-line text uses tabs between
labels and values. Native measurement read tabs as missing font-file glyphs,
while ImGui synthesizes a tab with four space advances. A regression using
Consolas at 24 pixels measured `Track:\tTest stage` at 224.320 pixels but rendered
it at 263.906 pixels before the fix. Right-aligning that underestimated width
places the end of the line outside the viewport.

Measurement now uses ImGui's tab width. The legacy `WRC Clean Roman` and
`WRC Clean Bold` family names also resolve to the bundled WRC files. Rendering
uses the requested scaled text size instead of rounding it a second time;
the atlas still caches rasterized integer sizes. This keeps fractional viewport
scaling consistent with Lua's measured layout.

Regression coverage includes tabs and CRLF, legacy font aliases, all seven
Map Information rows and their context values, right-edge bounds, and actual
rendered geometry compared to the measured font size at 100% and 75% scale.

Font-family verification also found that `WRC Clean` must select the bold file
even when the caller requests weight 400. `WRCCleanBold.ttf` declares family
`WRC Clean` / style `Bold`, whereas `WRCCleanRoman.ttf` declares family
`WRC Clean Roman` / style `Book`. Native selection now matches these original
families and the browser CSS: Roman/400 uses Roman; Roman/700 and both Clean/Bold
aliases use Bold. Tests verify the actual filenames and all six alias/weight
combinations. Liquid Glass's explicit Segoe UI selection is unchanged.
