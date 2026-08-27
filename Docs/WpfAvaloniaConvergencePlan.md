# WPF And Avalonia Convergence Plan

Status: active

Last reviewed: 2026-08-28

## Goal

Keep the mature Windows WPF product reliable while making Avalonia a tested,
maintainable desktop host for Windows and SteamOS/Linux. Shared behavior belongs
in platform-neutral services; each UI host should mainly own presentation and
platform integration.

This plan complements `FutureSteamOSAvaloniaMigration.md`. That document covers
the SteamOS architecture and delivery path. This document tracks product
quality, release consistency, host parity, and maintenance work across WPF and
Avalonia.

## Baseline

Recorded on 2026-08-27:

- All 80 portable tests pass.
- The Windows test project does not compile because an RBR test resolves
  `RBR.GameName` against its test namespace instead of the game class.
- WPF reports product version `2.99.99.47`; Avalonia reports `2.99.99.45`.
- Neither test project directly references the Avalonia host.
- `MainWindowViewModel` constructs `DesktopRuntime` directly and combines
  navigation, runtime snapshots, co-driver packages, dashboards, game settings,
  localization, and overlay state.
- Avalonia stages Codemasters and BeamNG providers. RBR and ACR assets are
  present, but those game providers are not staged by the Avalonia project.
- Replay, update, package-management, and complete diagnostics views remain WPF
  only.
- Avalonia compiled bindings are disabled globally and one UI dependency uses a
  nightly version.
- Canonical product assets and release text are still sourced from the WPF
  project directory.

## Progress

- 2026-08-28: Phase 0 completed. Portable and Windows tests pass, WPF and
  Avalonia use one canonical desktop version property, and the RBR portable-path
  resources now have parity across every supported locale.
- 2026-08-28: Phase 1 started. `MainWindowViewModel` now receives an
  `IDesktopRuntime` instead of constructing `DesktopRuntime`, runtime control
  operations no longer expose the concrete controls service, and a dedicated
  Avalonia headless test project covers startup snapshot mapping and control
  forwarding without starting real audio, game monitoring, or a desktop window.

## Principles

1. Do not duplicate domain behavior in two view models.
2. Do not block the SteamOS MVP on Windows-only parity such as native overlay or
   direct process injection.
3. Make platform support explicit instead of silently omitting a game or action.
4. Treat tests, version metadata, packaging, and update metadata as one release
   contract.
5. Prefer small host adapters over a shared UI abstraction.
6. Preserve the current WPF behavior while shared services are extracted.

## Phase 0: Restore The Release Safety Net

Tasks:

- Fix the Windows test compilation failure and run both test projects from a
  clean restore.
- Define the desktop product version in one repository-level build property.
- Make WPF and Avalonia consume that property while allowing an explicit host
  override if separate preview channels become necessary.
- Make packaging scripts derive filenames and displayed versions from the same
  source.
- Record and then reduce compatibility warnings from legacy WPF dependencies.

Done when:

- Portable and Windows tests compile and pass.
- A release version change requires editing one canonical property.
- WPF, Avalonia, installer metadata, and update metadata cannot silently drift.

## Phase 1: Make The Avalonia Host Testable

Tasks:

- Introduce an injectable runtime contract around `DesktopRuntime`.
- Inject scheduling/dispatch boundaries needed by the main view model.
- Add an Avalonia host test project or a headless test fixture.
- Cover startup snapshots, game switching, culture switching, dashboard state,
  co-driver selection, voice-package operations, and game-settings pane creation.
- Add smoke tests that load every primary Avalonia view and fail on unresolved
  bindings or resources.

Done when:

- Avalonia host behavior can be tested without starting audio, game monitoring,
  WebView, or a real desktop window.
- The main navigation and settings views load under automated tests.

## Phase 2: Complete High-Value Avalonia Workflows

Implement in this order:

1. Diagnostics and support export.
2. Update notification and download workflow.
3. Package management.
4. Replay browsing, playback, export, and capture where supported.

Underlying behavior must be shared with WPF. Only dialogs, navigation, native
window operations, and platform pickers remain host-specific.

Done when:

- A SteamOS user can diagnose startup, game detection, audio, WebView, and
  dashboard failures without a terminal.
- Feature availability is shown accurately for the current platform.

## Phase 3: Make Game Support Explicit

Tasks:

- Define a provider capability/support matrix by operating system and runtime.
- Stage RBR and ACR providers in Windows Avalonia if the Windows host is intended
  to replace WPF for those games.
- Keep Proton relay work separate from direct Windows integrations.
- Show a clear unsupported or relay-required state instead of shipping unused
  assets or silently omitting providers.

Done when:

- Build output, game list, settings, diagnostics, and documentation agree about
  which games are supported on each platform.

## Phase 4: Reduce Long-Term UI Maintenance Cost

Tasks:

- Split the large WPF and Avalonia view models by workflow after shared service
  boundaries are covered by tests.
- Enable Avalonia compiled bindings incrementally, one view at a time.
- Replace nightly or framework-incompatible UI dependencies with validated
  stable packages.
- Move canonical branding, licenses, release notes, and shared images into a
  neutral product-assets directory.
- Add a CI matrix for portable tests, Windows tests, Avalonia Windows build, and
  Linux/AppImage build.

Done when:

- Host projects contain presentation code and thin adapters rather than product
  workflow implementations.
- Binding failures, missing resources, version drift, and provider packaging
  mistakes are detected before release.
