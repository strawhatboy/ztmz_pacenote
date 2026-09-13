# Release resource source audit (2026-09-13)

Packaging now uses the verified online sources through
`build/stage_release_resources.ps1`. Follow-up investigation confirmed the 14 local-only WRC
names are obsolete names, not missing stages; do not merge them into the repository.

## Default audio

- After the user replaced the online asset on 2026-09-13, a fresh download
  matched the regenerated rc24 audio archive exactly:
  SHA256 `3b51c9c2982023ba7c4ad9cfe7be6b179688a229e89639ca8bd01a0017286ba0`,
  2,469,829 bytes, 7z format, 580 files under `default/`. Archive integrity
  passed, and the locally generated archive had already passed a complete
  extracted-file SHA256 comparison against rc24's audio. Use this new hash
  for future packaging. The observations below describe the replaced asset.
- URL: https://gitee.com/ztmz/ztmz_audio_pkgs/releases/download/v1.1/default.zpak
- SHA256: `df2c2acb44e81e8e2c8484f57591e64292a7013b262776a573ad0ea2a020af8d`
- Format: 7z, containing a `default` directory.
- All 361 files match the corresponding files under
  `C:\My Documents\My Games\ZTMZClub_nextgen\codrivers\default`
  byte-for-byte; no extra or missing files.

## Pacenotes

- Repository: `git@gitee.com:ztmz/dr2_pacenote_scripts.git`
- Audited commit: `b7b914354eb0c413e20988c37919f8ded52c1d77`
- The repository also contains `default/Assetto Corsa Rally`. Do not copy the
  whole default directory. Release selection must explicitly allow DR1, DR2,
  and EA SPORTS WRC only.
- DR1: 72 files on each side; names and text match after CRLF/LF normalization.
- DR2: 157 files on each side; names and text match after CRLF/LF normalization.
- WRC: online 265 files, local 267 files. All 253 common files match.
- Both local profile roots were checked with the same results:
  `C:\My Documents\My Games\ZTMZClub_nextgen\profiles\default` and
  `C:\Users\Andy\Documents\My Games\ZTMZClub_nextgen\profiles\default`.

Online-only WRC files (12):

- EKO ACROPOLIS RALLY GREECE: Drosopigi - Aghia Triada, Harvati,
  Irini - Schinos, Perachora, Pisia, Posidonia.
- RALLY SWEDEN: Ersboda, Ersmark, Haga, Sandbacka, Umeå Sprint, Umeå.

Local-only WRC files (14):

- BIO BÍO RALLY CHILE: Bio Bío, Chivilingo, El Poñen, Hualqui, Laja,
  Las Pataguas, María Las Cruces, Pulpería, Rere, Río Claro, Río Lía, Yumbel.
- RALLYE MONTE-CARLO: La Bâtie-Neuve - Saint-Léger-les-Mélèzes,
  Saint-Léger-les-Mélèzes - La Bâtie-Neuve.

### Rename investigation

- Commit `2dd58ce8d952d308e9e0b20bdd578325a0a9627c` (2024-10-23),
  `3.1: EA WRC 24 DLC rename CHILE location`, renames all 12 Chile files
  from `BIO BÍO RALLY CHILE_` to `RALLY CHILE BIO BÍO_` without changing contents.
- Commit `0ddad2acb162523b390db0663cf2ea0b09561527` (2025-03-25),
  `two tracks names changed, damn`, renames the two Monte Carlo files to
  `RALLYE MONTE-CARLO_La Bâtie-Neuve.pacenote` and
  `RALLYE MONTE-CARLO_Saint-Léger-les-Mélèzes.pacenote`, respectively.
- The current local files already contain these new names as well as the old
  ones. All new-name files match the online repository.
- Both old Monte Carlo files and six old Chile files match the corresponding
  online new-name files after newline normalization.
- The other six old Chile files (Bio Bío, Hualqui, Laja, Pulpería, Río Claro,
  Yumbel) are stale: current online files add a `-4` pacenote record and revise
  the final-record comment. They are not additional stages.
- The earlier suggested 279-file union was incorrect. The online set has 265
  files; the local 267-file set includes 14 obsolete aliases and lacks the 12
  Greece/Sweden files listed above. Use the 265-file online set when switching
  sources, rather than adding obsolete names back.

## Packaging implementation

- `build/release-resources.json` pins the audio URL, archive SHA256, repository
  commit, and each resource tree's file count and SHA256 manifest digest.
- The tree digest is SHA256 of UTF-8 text: ordinally sorted
  `<lowercase file SHA256>  <relative/path>` lines, joined with LF and a final LF.
  It binds filenames as well as bytes. Profile bytes come from `git archive`,
  avoiding checkout newline conversion.
- `publish.bat` and `package_acr_experimental.bat` download/export these sources
  and verify them before and after copying. There is no registry, Documents,
  `ZTMZ_PACKAGE_DATA_HOME`, or personal data fallback.
- `package.bat` and `package_signed.bat` recheck the staged resources before
  invoking Inno Setup. `onekey.bat` uses these same steps.
- Only the three named profile directories are exported; ACR is not exported.
  Unexpected resource files/directories fail validation. Destinations must be
  empty before staging, preventing stale files from merging into a new release.
- Downloads and bare git objects are cached under ignored `build/resource-cache/`.
  Audio is hashed on each use; exported git trees are checked against the pinned
  digests on each use. Cached resources allow subsequent builds without network.
- Prerequisites: PowerShell, curl.exe, Git with Gitee SSH access, and 7-Zip
  (on PATH or under Program Files). Hashing uses .NET, not Get-FileHash.
- To update resources deliberately: review the new archive/commit, calculate
  tree digests with `Get-ReleaseTreeDigest` from `release_resource_helpers.ps1`,
  and update the manifest. A changed remote file is never accepted silently.
- Regression checks: `powershell -NoProfile -ExecutionPolicy Bypass -File
  build/test_release_resources.ps1 -ValidStagingRoot <verified-staging-root>`.
  Tests operate on a temporary copy and check missing/extra/modified files,
  forbidden ACR directory, missing audio, and valid/restored resources.

The installer already generated before this change is not rewritten by changing
these scripts. Run `build/onekey.bat` again to package the 265-file WRC source.
