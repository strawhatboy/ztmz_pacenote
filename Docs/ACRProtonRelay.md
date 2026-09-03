# Assetto Corsa Rally Proton Relay

## Purpose

The native Linux Avalonia application cannot directly consume ACR's Windows
named shared-memory pages or the named pipe created inside its Proton prefix.
It also must not depend on Wine's internal Linux process layout to inject a
Windows DLL.

The ACR integration therefore keeps the Windows ABI boundary inside Proton:

```text
Linux Avalonia host
    <--- versioned authenticated loopback TCP --->
ACRProtonRelay.exe in ACR's Proton prefix
    <--- Windows shared memory / named pipe --->
acr.exe + ACRFmodLivePacenoteBridge.dll
```

The DLL remains Win64 because the target process is Win64. The provider,
telemetry model, token translation, and relay protocol are portable .NET.

## Components

- `tools/ACRPacenoteTrace/ACRProtonRelay.cpp` implements the Win64 relay.
- `ACRFmodLivePacenoteBridge.dll` remains the in-process FMOD hook and writes
  its existing JSON messages to `ZTMZPacenote.ACR.Live.v1`.
- `ACRProtonGameDataReader` owns a loopback listener on an ephemeral port and
  converts relayed memory pages through the same managed ACR structures used
  on Windows.
- `ACRProtonBridgeController` locates the running ACR process, its Proton tool,
  `STEAM_COMPAT_DATA_PATH`, installation path, and starts the relay.
- `ACRProtonProcessLocator` reads `/proc` and also walks process parents when
  the Proton executable is not present in the ACR command line.

ACR advertises Steam App ID `3917090`, allowing the shared Linux process
detector to identify Proton wrapper processes as well as `acr.exe` itself.

## Protocol v1

The relay connects only to `127.0.0.1`. The host chooses an ephemeral port and
a random 192-bit token for each game-reader session. The first frame must be an
authenticated hello message.

Every frame has a 12-byte header:

```text
0..3   ASCII "ZAR1"
4      protocol version (1)
5      frame type
6..7   reserved
8..11  unsigned payload length, network byte order
```

Frame types are:

- `1`: hello JSON containing the session token and relay version;
- `2`: telemetry containing the raw graphics, physics, and static pages;
- `3`: one UTF-8 live-pacenote JSON message;
- `4`: relay status JSON.

Telemetry has fixed v1 sizes of 1588, 800, and 820 bytes. Tests assert that
these sizes still equal the managed `Pack = 4` structure layouts. Any future
layout change requires a protocol-version or capability update rather than an
unannounced size change.

The protocol rejects unknown versions, unknown frame types, and payloads over
1 MiB. The listener rejects a connection whose first frame or token is wrong.

## Launch and injection

The Linux controller performs these checks before starting Proton:

1. ACR is running and belongs to App ID `3917090` when that environment value
   is available.
2. The exact Proton executable and compatdata path can be recovered from the
   running process.
3. `acr.exe` and ACR's FMOD Studio library match the packaged bridge manifest.
4. The relay, hook DLL, and manifest are present in the provider bundle.

The relay uses Win32 process enumeration inside the same Proton prefix to find
`acr.exe`. It calculates the target `LoadLibraryW` address from the target
process's `kernel32.dll` base plus the local export RVA, instead of assuming
that the local function address is valid in another process.

Injection remains explicit and opt-in through the existing ACR integration
setting and action. The bridge is intended for offline/private testing and is
unloaded when ACR exits.

## Packaging

The portable ACR provider output includes:

```text
games/ZTMZ.PacenoteTool.AC.dll
games/ACRExperimentalBridge/ACRProtonRelay.exe
games/ACRExperimentalBridge/ACRPacenoteTraceInjector.exe
games/ACRExperimentalBridge/ACRFmodLivePacenoteBridge.dll
games/ACRExperimentalBridge/acr-live-bridge.manifest.json
```

Both Avalonia build and publish staging copy this bundle on Windows and Linux.
The Windows WPF implementation continues to use its existing direct shared
memory and named-pipe path.

## Validation status

Verified in the repository environment:

- the relay builds as Win64 with the static MSVC runtime;
- native relay and managed protocol agree on hello/status framing over TCP;
- both `net10.0` and `net10.0-windows` ACR provider targets build;
- Windows Avalonia output contains the provider, languages, image, and bridge;
- an Arch Linux VM builds the complete self-contained AppDir, including all
  required ACR provider and bridge payloads;
- portable tests cover fragmented frames, size limits, structure sizes,
  Steam identity, Wine path conversion, case-sensitive filesystem handling,
  parent-process Proton discovery, and rejection of a mismatched Steam App ID;
- all 97 portable tests pass on both Windows and the Arch Linux VM.

Still required on a machine with ACR installed under Proton:

- validate that the selected Proton version permits the Win32 injection APIs;
- verify all three shared-memory pages and the FMOD pipe are visible to the
  relay in the same prefix;
- test game and ZTMZ restart ordering, suspend/resume, and ACR updates;
- confirm operation inside SteamOS Gaming Mode's pressure-vessel environment.
