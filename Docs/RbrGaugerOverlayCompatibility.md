# RBR / GaugerPlugin DX9 startup crash

## Evidence (2026-10-17)

Starting the installed ZTMZ while RBR was already racing displayed a detour
failure and crashed RBR. The game's minidump at 19:41:40 shows an execute access
violation at address zero, returning to `GaugerPlugin.dll+0x2fcc`, with the ZTMZ
overlay below it on the stack.

Disassembly identifies the preceding instruction at `GaugerPlugin+0x2fc6` as
`call [0x1007ab60]`: its saved CreateDevice trampoline. GaugerPlugin hooks
`Direct3DCreate9`, then attempts to hook CreateDevice again whenever that API is
called. It stores the detour result back into its trampoline even on failure.
Its binary contains `Failed Detour CreateDevice.`. ZTMZ's generic DX9 discovery
created another API/device in the game process, triggering this unsafe re-entry.
The crash happened before any new RBR FFB implementation was built or loaded.

## Fix

For the verified RBR SSE executable (SHA256
`5BB1B0C8F6719FAECAD63B0AAFD7541089D1D1FF7A4C9BB68ED49BB53DC6F63B`), discover
the game's existing device through the RBR SDK chain:
`[ [ [0x7EA990] + 0x28 ] + 0xF4 ]`. Read its Reset/Present targets from vtable
slots 16/17 using checked reads and confirm the chain did not change. Validate
the targets are executable memory. This discovery does not call any COM method,
take a device reference, or create a D3D9/D3D9Ex API, window or device.

Unsupported RBR builds, a missing device, invalid targets, or a changed pointer
chain fail without falling back to probe creation. An unrecognized host with
GaugerPlugin loaded also refuses generic probing. Other games retain the normal
DX9/DX9Ex discovery. Installation rollback removes incomplete hooks so retrying
does not collide with an abandoned hook record.

Both x86/x64 builds and existing-device validation tests pass. Both DX9 graphics
lifecycle tests pass with desktop GPU access (render, stop/start and reset).
Sandboxed graphics tests cannot create their baseline device: `0x8876086A`, before
the overlay loads. Live verification passed after installing the patched x86
DLL: RBR loaded GaugerPlugin and the overlay together, and the user confirmed
the dashboard appeared correctly without the detour message or crash.

The original installed x86 DLL was backed up under
`tmp/ztmz_dashboard_overlay-before-rbr-fix.dll`; the patched installed DLL SHA256
is `78A0968D9150B135FDD21FD5F5D660FEDDD2492126F6D618284BFE6A6AB273E7`.
