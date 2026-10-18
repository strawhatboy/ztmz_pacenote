# Dashboard component failures

WPF and Avalonia use the same Windows overlay controller. A missing or inaccessible
in-game dashboard component is a dashboard failure, not an application or audio
failure. The controller keeps the chosen backend and exposes the reason and exact
component path to the dashboard page, with details available for copying. It does
not report an active separate-window fallback when none was started.

The loader opens and validates the overlay DLL and (for a 32-bit game) its x86
helper before starting any remote load. Opening the files preserves Windows access
errors that `File.Exists` would hide. A preflight failure does not consume the
single remote-load attempt for that game process. An uncertain load timeout still
requires a game restart; the loader must not repeatedly inject on an ambiguous
result.

## Recovery

- Missing component: inspect the named path and antivirus protection history. A
  missing file alone does not establish that an antivirus removed it. Repair the
  installation using the official package once any reported detection is resolved.
- Access/security block: inspect file permissions and security-software history.
  Windows errors 225/226 explicitly indicate a security detection; access denied
  alone does not identify the cause. The UI does not advise disabling protection.
- Invalid component: repair the matching release's native-dashboard bundle. Do not
  mix DLLs and helpers from different architectures or versions.
- Other loading failure: use the copied diagnostics; restart the game before
  retrying if a load may already have been attempted.

The existing rendering-backend selector can choose separate-window dashboards.
Pacenote playback does not depend on the injected dashboard DLL. No automatic
backend switch, DLL re-download, antivirus exclusion, or repeated injection is
performed by this recovery UI.

Failure status is cleared for a new attachment or detach. A late failure from an
obsolete attachment cannot overwrite a newer session's status.

## Verification

Portable tests distinguish missing, blocked, invalid and unknown outcomes and
preserve diagnostic context. Windows tests check actual file preflight failures
without injecting into a game and verify that the controller exposes an unavailable
status instead of implying an active fallback. Desktop tests exercise presentation
and clearing the details when a later snapshot recovers.
