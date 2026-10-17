# Windows startup launcher

The WPF installer launches `ZTMZ.Launcher.exe`. This is a native x64 program
with a statically linked C++ runtime; it can explain a missing .NET installation
before the managed application can run. It monitors startup only and exits when
WPF reports that initialization and its queued UI callbacks have completed.

## Check order

1. Resolve the data folder using an explicit `ZTMZ_DATA_HOME`, otherwise HKCU
   `Software\ZTMZ Next Generation Pacenote Tool\ZTMZHome`, otherwise
   `Documents\My Games\ZTMZClub_nextgen`. An inaccessible registry value or a
   missing/unwritable configured folder is an error, not a reason to silently
   create a replacement profile. Locate binaries beside the launcher itself;
   the installer registry value is diagnostic information only.
2. Read configuration JSON without changing it. Missing settings files are
   allowed because the application creates defaults. Malformed JSON offers an
   explicit backup/reset of that file only. Legacy/base and game settings allow
   additional Json.NET syntax; parse failures there are deferred to the managed
   parser to avoid rejecting otherwise valid settings. Other initialization
   failures are reported without guessing which settings should be reset.
3. Check the required files listed in `ZTMZ.startup.json`, generated from the
   host dependency graph and essential bundled assets. Third-party satellite
   language DLLs remain intentionally excluded.
4. Ask the installed x64 `hostfxr` resolver to validate the actual application
   runtime configuration, including Desktop/ASP.NET frameworks and roll-forward.
5. Check WebView2. Users can continue without it, with web/editor features
   unavailable.
6. Start the managed executable and monitor its PID-specific progress report.
   Report early exit, initialization errors, or a startup exceeding 60 seconds.
   A slow process is never killed automatically; users can keep waiting.

## Recovery and reports

The error dialog can open the local report, retry, open an official runtime
download page, or select an existing data folder. Selecting a folder requires
confirmation before updating `ZTMZHome`; an environment override is changed only
for that launch. It does not move data.

Reset requires a separate confirmation. It atomically renames the affected
configuration to `*.startup-backup-<pid>-<tick>`, preserving its bytes and never
overwriting a backup. Restore it manually while ZTMZ is closed if needed. Voice
packages, databases, profiles, scripts and replays are not reset.

Reports and captured host error output are stored in `%LOCALAPPDATA%\ZTMZ\Startup`
(or a `ZTMZ-startup-*` directory in the user's temporary folder if that location
is unavailable). They include paths, failure details and startup stages. Reports
are local; the launcher does not upload them.

`ZTMZ.Launcher.exe --diagnose --report "C:\path\report.json"` performs checks
without starting WPF or showing dialogs. Exit 0 means checks passed; exit 2 means
a check failed. `--headless` also suppresses dialogs during process monitoring;
exit 3 means the child was still running when monitoring stopped.

Opening the managed executable directly bypasses native preflight, although the
early managed exception boundary still provides a basic error dialog. Existing
Windows-startup shortcuts are updated on successful initialization when that
setting is enabled. Migration helpers launch the managed host directly and clear
the parent's startup-status environment variable.

This does not promise recovery from every Windows/security-software failure:
Windows must be able to run the native launcher. Runtime failures after readiness
remain the application's responsibility. This launcher currently targets WPF;
Avalonia packaging is unchanged.

## Build and verification

WPF imports `build/StartupLauncher.targets`; normal build and publish compile and
stage the launcher, dependency manifest and JSON-library license. Consequently
`build/onekey.bat` includes them. Desktop, Start-menu and installer launch actions
target the launcher. The payload verifier requires a native x64 PE with no CLR
header and checks that managed dependencies are in the manifest.

Build native tests with:

```powershell
powershell -File build/build_startup_launcher.ps1 -BuildDirectory build/startup-launcher -RunTests
powershell -File build/test_startup_launcher.ps1
dotnet test src/ZTMZ.PacenoteTool.Tests/ZTMZ.PacenoteTool.Tests.csproj -c CountdownCheck --filter FullyQualifiedName~StartupReportTests
```

The integration script requires a published payload and the installed runtimes.
It uses an isolated test mutex and harmless child executable to exercise actual
process launch, readiness, failure, timeout and stale-PID handling without closing
the user's running ZTMZ instance or modifying their data/registry. Test binaries
are never copied to the release payload. For release verification, compile the
installer, extract it with `innounp -x -a -b`, then run
`build/verify_installer_payload.ps1` against the extracted tree.
