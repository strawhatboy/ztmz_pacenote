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

## Earlier rc28 release verification (2026-10-17, superseded artifact)

The clean WPF publish previously tried to copy the KT provider without building it.
`publish.bat` now explicitly publishes KT (`net10.0`, `win-x64`) before the WPF host.
The payload verifier also checks every bundled dashboard, web-client and font file
against source hashes to catch missing or stale assets.

Validation completed for 2.99.99.56-rc28:

- Clean `build/publish.bat`, including x64/x86 native builds and payload/resource checks.
- 59 focused .NET tests covering DR2 vehicle/turbo data, wire serialization,
  Liquid Glass and language changes; all 18 browser Lua dashboard smoke checks,
  including Chinese/English switching; native client, Lua runtime and bundled Lua checks.
- Avalonia Windows Debug build passed. Existing compiler/XAML warnings remain.
- Inno Setup compiled `Output/ZTMZClub_PacenoteTool_Installer_2.99.99.56-rc28.exe`
  (32,861,394 bytes), product version `2.99.99.56`.
  SHA256: `d96ea959624ff0f09fc88e857b420400c60bb02cf7c1e8ec4380a29bbd9524b5`.
  Existing missing Chinese Inno messages fall back to English.

This verifies compilation and staged contents, not a fresh installation or upgrade.
Installer launch/upgrade and live gameplay smoke testing remain the final release
gate. No installer was uploaded and `autoupdate_beta.json` was not advanced.

## Final rc28 packaging audit (2026-10-17)

The installer above predates the later DR2 countdown, graphical-pacenote dispatch,
release-note and update-dialog changes. It has been replaced by a clean publish
and a new locally signed installer containing the current working tree.

Correction: the initial audit incorrectly called 41 publish/installer differences
missing required files. **39 third-party satellite resource DLLs were intentionally
excluded**; their temporary reintroduction has been reverted. The nested
`runtimes/win-x64/native/WebView2Loader.dll` is byte-identical to the already-packaged
root loader and remains excluded too. Only `ThirdPartyNotices/Aim_2_MoTeC.txt`
needed adding to the installer. ZTMZ's own language JSON files remain included.
The verifier checks the root loader and source hashes for provider/core language
files, dashboards, fonts and web assets; it does not require satellite assemblies.

`build/verify_installer_payload.ps1` compares an extracted installer against every
published file and the directly packaged databases. It requires explicit handling
of any new publish directory. Root CSV/XAudio exports and provider `.deps.json`
files and third-party satellite assemblies are deliberately excluded; the duplicate
root ACR bridge is verified at its canonical `games/ACRExperimentalBridge`
destination. The nested WebView2 loader is checked against the installed root copy.
The archive verifier rejects unexpected satellite DLLs to preserve this packaging
decision. These installer rules are used by `build/onekey.bat` through
`publish.bat` and `package_signed.bat`; no manual file copying is required.

Historical rc28 artifact before the native launcher:

- `Output/ZTMZClub_PacenoteTool_Installer_2.99.99.56-rc28.exe`
- Product version `2.99.99.56`; **32,887,184 bytes**.
- SHA256: `c38426b1ef7e1980e4cc9926684663e72d36f0a77b38262bb3322e6a4dc41d04`.
- Timestamped signature from the existing `CN=ZTMZClub` self-signed certificate.
  Windows reports an untrusted root on this machine; this is not a publicly trusted
  code-signing certificate. No trust-store changes were made.

Validation:

- Clean `publish.bat` and `package_signed.bat` passed. Inno's five existing missing
  Chinese extraction messages still fall back to English.
- Extracted the final installer into an empty directory with `innounp -x -a -b`.
  **2,134 destination/content comparisons passed**, with intentional exclusions
  accounted for. No satellite resource DLLs are installed. The corrected verifier
  rejects the mistakenly expanded archive with 39 unexpected language DLLs.
- Verified all five providers, WRC 7/9/Generations and ACR bridges, BeamNG mod assets,
  x86/x64 overlays and the x86 injector, both installed font locations, 18 dashboards,
  346 pacenote icons, Monaco, web assets, language files and current release notes.
- Pinned resources passed: default voice package 580 files; DR1 72, DR2 157 and
  EA WRC 265 profile files. Seven resource validation scenarios passed, including
  rejection of missing, modified and extra files. Ten installer path scenarios passed.
- Portable suite: **737/737 passed serially** with an isolated data directory.
  The initial parallel run failed seven tests during competing first-use writes to
  `Config.Load`, plus one WRCG pipe test. Serial execution passed both categories.
  This does not establish thread safety for concurrent first initialization of Config;
  WPF initializes Config in its application constructor before runtime startup.
- Windows suite: the rebuilt full run passed 469/470; the remaining failure was
  missing ACR translations. After completing them, all 15 localization tests passed.
  Earlier failures also identified missing overlay/RBR messages and a stale database
  assertion of 349 tokens. All eight additional languages now include the 15 new
  messages; the database test checks nonempty translations for every actual token
  (currently 354), retaining its mechanical-damage and fallback assertions.
- Browser smoke tests: all 18 dashboards passed. Native x64: seven protocol/runtime/
  font/backend checks passed, including all bundled dashboards. Native x86: Lua,
  bundled dashboards and the RBR DX9 device check passed.
- Before restoring the intentional exclusions, started a copy of the extracted
  application (the corrected installer has the same application binaries), with data and a
  fresh isolated configuration selected through `ZTMZ_DATA_HOME`. It loaded all five
  providers and 18 dashboards, initialized HomePage/DR2, prepared the default voice
  preview and remained responsive. No fatal or missing-file startup error occurred.
  The sandbox-blocked online update request was logged as a recoverable error.
  The temporary application process was stopped after the check. The user's installed
  app/data and registry were not replaced.

To repeat the archive check after packaging (use an empty extraction directory):

```powershell
innounp -x -a -b -dtmp/release-56-extracted Output/ZTMZClub_PacenoteTool_Installer_2.99.99.56-rc28.exe
powershell -NoProfile -File build/verify_installer_payload.ps1 -ExtractedRoot tmp/release-56-extracted
```

Packaging and isolated startup are verified. A real clean-machine install and an
upgrade from rc27 remain the final release gate; extraction does not exercise runtime
downloads, installer execution, or upgrade preservation. This is a framework-dependent
release: .NET 10 Desktop/ASP.NET Core and WebView2 prerequisites are handled by the
installer, not bundled as offline runtime installers. The latest gameplay fixes also
still warrant a short installed-build smoke test. At audit completion the changes
were uncommitted and no installer had been uploaded. `autoupdate_beta.json` still
advertises `2.99.99.55` until the release asset is published.


## Native startup launcher: 2.99.99.57-rc29 (2026-10-17)

The WPF payload now includes `ZTMZ.Launcher.exe`, `ZTMZ.startup.json`, and
`ThirdPartyNotices/Launcher-nlohmann-json.txt`. The launcher is built and staged
by the WPF project, so the existing `onekey.bat` workflow includes it. Installer
shortcuts and the post-install launch action target the launcher. Third-party
satellite language DLLs remain excluded.

The launcher belongs to the next release, `2.99.99.57-rc29`; rc28 is already
released. The initial local launcher test artifact had incorrectly reused the rc28
filename and has been moved out of `Output` to prevent distribution. The correctly
versioned installer is:

- `Output/ZTMZClub_PacenoteTool_Installer_2.99.99.57-rc29.exe`
- **32,999,176 bytes**; SHA256 `5b3f9aa2c8c15791305d10dc97bea7a23485b29e48e083bd2f58b5a9c946779e`.
- Signed using the existing `CN=ZTMZClub` certificate and DigiCert timestamp.
  The existing self-signed certificate's trust limitations still apply.

Validation for this follow-up:

- Native policy tests: **39 checks passed**, including configuration precedence,
  missing-folder behavior, UTF-16 settings, argument quoting, safe backup and
  collision handling, manifest traversal rejection, readiness/exit ordering,
  and a temporary file lock during atomic report replacement.
- Process integration: **11 scenarios / 19 assertions passed** with a harmless
  child process and an isolated mutex. Covers missing data/settings/files/runtime,
  accepted legacy Json.NET syntax, healthy preflight, readiness, explicit failure,
  early exit, slow startup and rejection of another PID's status.
- Managed startup reporting: **6 tests passed**, including terminal-state ordering,
  concurrent callbacks, direct launch, unwritable output and a transient reader lock.
- Official `publish.bat` and `package_signed.bat` passed. The new executable is a
  native x64 PE without a CLR header and imports only Windows system DLLs.
- Extracted installer: **2,137 destination/content comparisons passed**, including
  the three new payload files. No third-party satellite resource DLLs were installed.
- Ran the extracted production launcher with `--diagnose` and `ZTMZ_DATA_HOME`
  pointing to the extracted data: **checks_passed**, runtime resolver result 0,
  WebView2 available. The corrected rc29 installer was re-extracted and passed
  the same 2,137 comparisons and preflight; its packaged WPF file version is
  `2.99.99.57`. No installed data or registry setting was changed.

The user's installed ZTMZ instance remained running. A full new WPF UI launch
through this launcher has therefore not been exercised in this follow-up; the
process protocol was tested using fixtures. A clean-machine install/upgrade and
that final interactive startup check remain release checks. The installer was
not uploaded. `autoupdate_beta.json` continues advertising the released
`2.99.99.56-rc28`; the new launcher notes are in a separate rc29 section in all
three changelog files, with released rc28 notes preserved. See
[startup-launcher.md](startup-launcher.md) for behavior and recovery details.
