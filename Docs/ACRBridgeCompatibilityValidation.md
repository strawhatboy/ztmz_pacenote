# ACR bridge compatibility validation

## 2026-09-10 — FMOD Studio 2.3.14 (build 164239)

Validated on Windows against the installed Assetto Corsa Rally build:

- `acr.exe` SHA-256: `42C359B0CD24774A86441710FDC7D7DB3BC06B57B3FF30CB9EA5D56FB71448FB`
- `fmodstudio.dll` SHA-256: `3132A935C312FB6FFFC8FEB71AF070837341EFF95EF12F105E7E7D6A4D0993B0`

The existing bridge resolves the named `FMOD::Studio::System::getSoundInfo` export with `GetProcAddress`; it does not use a fixed function offset. The same decorated export name exists in the updated library.

The existing injector successfully loaded the bridge into the running game. The bridge log recorded `bridge-started` at 19:50:08, after successful MinHook initialization and activation. The user then drove and confirmed that ZTMZ pacenote playback worked normally. The inspected log contains startup confirmation, not per-call playback evidence; playback validation is the user's observation.

Updated `src/ZTMZ.PacenoteTool.AC/ExperimentalBridge/acr-live-bridge.manifest.json` to this tested pair. The schema supports one verified pair. No new Proton runtime validation was performed.

## Unknown-version confirmation

Windows and Proton now inspect the FMOD PE export table without loading the DLL into ZTMZ. The library must be AMD64 and expose the expected decorated `getSoundInfo` name pointing into an executable section; forwarded, missing, malformed or unreadable exports are rejected. This check applies even to the verified pair.

A hash mismatch produces a confirmation request rather than immediate injection. The WPF and Avalonia game-settings actions and the WPF startup prompt display the unverified-version warning with explicit Try/Cancel choices. Cancelling does not inject. Confirmation retries validation and is bound to the exact executable/library hash pair; changed files require fresh confirmation. Consent is not persisted. Interface checks and manifest/read failures cannot be overridden.

The interface check is a compatibility preflight, not a guarantee of runtime ABI or audio naming compatibility. A successful DLL load is likewise not proof of working pacenote playback.

Validation: WPF and Avalonia builds; portable tests covering valid exports, a name present but not exported, forwarded exports, malformed and missing libraries. The unknown-version dialogs have not yet been manually exercised in a running game.

Previous pair for reference:

- `acr.exe`: `97547892D1F5267C3F9A7FD446B1F33CFE108DE826C40DB4E153F9564B18066D`
- `fmodstudio.dll`: `DC61FAE2E70EC3A16FA78B678A0FC9FF1852491077A21C2B9C11BF4D160207FF`
