# ACR Pacenote Converter

Converts an ACR pacenote DataTable exported as JSON into ZTMZ `.pacenote` scripts.
The game is not modified or injected. ZTMZ triggers the generated records with the
read-only ACR shared-memory distance field.

## Export a table to JSON

Use FModel's `Save Properties` action on each `DT_Pacenote...` table. It writes
the decoded CUE4Parse JSON alongside the extracted `.uasset`. The raw `.uasset`
exports are UE5 Zen package slices and cannot be consumed by UAssetGUI directly.

## Convert to a ZTMZ profile

The default output filename is the canonical ACR DataTable name. Keep that default:
the ACR reader resolves localized shared-memory titles through `stage-aliases.json`
and uses the generated route-length header to distinguish full, cut, and reverse
variants.

```powershell
dotnet run --project tools\ACRPacenoteConverter\ACRPacenoteConverter.csproj -- `
  tools\ACRPacenoteConverter\exports\DT_PacenoteHafrenNorthFullForward.json `
  "C:\My Documents\My Games\ZTMZClub_nextgen\profiles\default\Assetto Corsa Rally"
```

Use `--dry-run` first to verify the target path. The converter writes
`ACR-unmapped-tokens.txt` next to the scripts for every source token that needs a
deliberate ZTMZ mapping; it does not silently discard those calls.

## Build stage aliases from localization

The exported localization resources contain canonical `CH_...` stage keys and their
localized display titles. Generate an alias file from all installed languages:

```powershell
dotnet run --project tools\ACRPacenoteConverter\ACRPacenoteConverter.csproj -- aliases `
  tools\ACRPacenoteConverter\stage-aliases.json `
  "C:\Users\Andy\app\Output\Exports\acr\Content\Localization\Game"
```

Each entry maps the canonical key to an extracted `DT_Pacenote...` table and records
the localized title for each language. This is the source for ACR's language-neutral
profile selection. The command accepts both original `.locres` resources and FModel's
decoded `Game.json` files; the latter add exact `TRACK_*` labels for partial layouts.

For partial layouts, ACR stores the table entries in the parent stage's spline
coordinate system. The converter detects the matching full route and normalizes
each cut/short profile to its own start position while preserving the full route's
initial call distance.
