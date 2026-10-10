# ACR Pacenote Trace

## Setup

The MinHook dependency is a Git submodule. Before building any helper, initialize it:

```bat
git submodule update --init --recursive
```

This directory contains the development trace hook and the packaged ACR live
bridge helpers.

It hooks the Dumper-7-generated `UObject::ProcessEvent` offset for the exact
game build and appends only calls whose object or function name contains
`Pacenote` to `tmp/acr_pacenote_trace.log`. It does not block or alter calls.

The DLL is intentionally not included in application packaging. It must be
rebuilt after an ACR update and should only be used in an offline session.

## Packaged Live Bridge

Run `build.bat internal-bridge` to build the release bridge bundle:

- `ACRFmodLivePacenoteBridge.dll` hooks ACR's FMOD event calls and writes
  countdown, pacenote, damage, and stage-state JSON to the existing named pipe.
- `ACRPacenoteTraceInjector.exe` is the Windows-host injector.
- `ACRProtonRelay.exe` runs inside ACR's Proton prefix. It injects the same
  bridge DLL, reads the three Windows shared-memory pages, and relays telemetry
  plus named-pipe messages to the native Linux application over authenticated
  loopback TCP.

The native-only command writes all three files to `bin/internal-bridge`.
Normal `dotnet build/publish` compiles them through the ACR project and copies
them into its output bundle; generated binaries are not tracked in Git.
See `Docs/ACRBridgeBuild.md` for build requirements and Linux artifact handoff,
and `Docs/ACRProtonRelay.md` for protocol and launch details.
