# Game Mod Catalog Integration

## Purpose

Game Mod discovery is deliberately separated from desktop UI code and from release-specific metadata:

- `ZTMZ.PacenoteTool.Base` defines the catalog records and capability interfaces.
- A game plug-in exposes a stable catalog game ID, a repository URL, and a game-specific manager.
- `game-mods/repo.json` owns Mod names, versions, package URLs, hashes, archive layout, and conflicts.
- WPF and Avalonia render generic catalog panes. A game plug-in contains no WPF or Avalonia Mod UI.

Updating or adding a catalog entry therefore does not require rebuilding the game plug-in.

## Capability contract

A game that supports managed Mods implements `IGameWithModCatalog`:

```csharp
public string ModCatalogGameId => "rbr";
public Uri ModRepositoryUri => new("https://gitee.com/ztmz/game-mods/raw/master/repo.json");
public IGameModManager ModManager { get; } = new RBRModManager();
```

The manager receives the complete game catalog for status checks and operations. This allows it to enforce `conflictGroup` rules without embedding the catalog in the game DLL.

## Repository format

The catalog currently uses schema version 1. Each Mod contains:

- Stable `id` and localized `names` / `descriptions`.
- A concise `author` and a stable display `version`; the original version text is preserved (for example `2026.8.27+1.069`, combining the package and bundled co-driver revisions).
- Optional structured `credits`, with localized `roles` and a list of `names`. Older clients ignore this field and continue to display `author`.
- HTTPS `downloadUrl` and `releasePageUrl`.
- SHA256 of the exact release asset.
- `packageRootDirectory`, or an empty value when files are directly in the ZIP root.
- Optional `externalFolderName` for the directory created in an external manager.
- Optional `externalFolderPrefix` for detecting older package versions.
- Optional `archiveEntryCodePage` for legacy ZIP filenames without UTF-8 metadata.
- Optional exact `preserveExistingPaths` entries for user-owned configuration.
- Optional `conflictGroup`; only one member may be installed or enabled.

The client accepts HTTPS catalogs only, limits the response to 1 MiB, validates all identifiers, URLs, hashes and relative paths, and caches the last valid catalog for offline use.

When a release asset is replaced, its catalog SHA256 must be updated. The Luppis V3 / Lao Wang package was repacked on 2026-09-03 to place `Audio/` and `Plugins/` directly at ZIP root. Its catalog entry consequently uses an empty `packageRootDirectory`, CP936 filename decoding, and SHA256 `4818B77E1E7349B9267FA45B0ED9988485A93C19D64750DE37F926FF862146DB`.

## RBR direct installation

Direct installs are managed under `<RBR>/.ztmz-mods`:

- Download to an operation-specific temporary directory.
- Verify SHA256 before extracting.
- Reject absolute paths, traversal, alternate roots, and entries outside a declared package root.
- Check disk space for installed data and rollback copies.
- Back up files that existed before the first managed install.
- Copy files transactionally and roll back mutations if installation fails.
- Store the package hash, installed-file hashes, and original backup paths in a per-Mod manifest. A replaced asset with the same display version is therefore still offered as an update.
- Preserve only catalog-declared existing user configuration files.
- During uninstall, restore original files or remove ZTMZ-owned files only when their current hash still matches. User-modified files are retained and reported.

RBR must be closed for every operation that changes game files.

## JSGME integration

JSGME is optional and is never bundled by ZTMZ.

- A fresh `JSGME.exe` without an INI is recognized with the conventional `MODS` directory.
- If root `JSGME.ini` exists, `[MODS FOLDER]/Name` is authoritative. This supports localized and renamed repositories such as `MODS 插件集合包`.
- The repository's own `JSGME.ini` `[MODS]` section is authoritative for enabled state. `!INSTLOGS` is not used because it can be stale.
- “Add to JSGME” stages and atomically moves a package into the repository. It does not enable the Mod.
- “Open JSGME” launches the detected executable with the RBR root as working directory.
- A directly installed Mod and a JSGME-enabled copy are reported as a conflict.
- Older JSGME package versions are detected through `externalFolderPrefix`.

## Adding another game

1. Add the game and Mods to `game-mods/repo.json`.
2. Implement `IGameWithModCatalog` in the game plug-in.
3. Implement an `IGameModManager` that understands that game's install location and external manager, if any.
4. Keep all game-specific filesystem and process logic in that game plug-in.
5. Add tests for package shape, rollback, existing-file ownership, conflicts, and external-manager state.

The WPF home page and Avalonia game settings page discover the capability automatically; no game-specific controls should be added there.
