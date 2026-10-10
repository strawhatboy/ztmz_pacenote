# Building the ACR bridge

The three Windows x64 bridge binaries are generated artifacts, not Git inputs.
`ZTMZ.PacenoteTool.AC` imports `build/ACR.NativeBridge.targets`: ordinary
`dotnet build` and `dotnet publish` compile them incrementally on Windows with
Visual Studio C++ build tools (located using vswhere). Outputs live under the
project's intermediate directory, isolated by build configuration and framework.
Both desktop hosts stage the provider and bridge into `games/ACRExperimentalBridge`.
The WPF batch scripts use those same project outputs, without another native build.

The bundle contains `ACRFmodLivePacenoteBridge.dll`,
`ACRPacenoteTraceInjector.exe`, `ACRProtonRelay.exe`, and the tracked
`acr-live-bridge.manifest.json`. Missing binaries fail the build rather than
silently producing an incomplete package.

## Linux / AppImage

These are Windows binaries even on Linux: the relay runs inside the game's
Proton prefix. Build them from the same source revision on Windows first:

```powershell
dotnet build src/ZTMZ.PacenoteTool.AC -c Release -f net10.0
```

Transfer `src/ZTMZ.PacenoteTool.AC/bin/Release/net10.0/ACRExperimentalBridge`
to an artifact directory on the Linux build host, then pass its absolute path:

```sh
dotnet publish src/ZTMZ.PacenoteTool.Avalonia -c Release -r linux-x64 \
  -p:AcrBridgePrebuiltDirectory=/path/to/ACRExperimentalBridge
```

The same property works with `dotnet build`, and must be forwarded by any
packaging wrapper invoking MSBuild. The AppImage script accepts it through
`ZTMZ_ACR_BRIDGE_SOURCE=/absolute/path/to/ACRExperimentalBridge`.
No Windows compiler is invoked on Linux.
A missing or incomplete supplied bundle produces a build error. Keep artifacts
outside source control; refresh them when native sources change.

For a native-only developer build, `tools/ACRPacenoteTrace/build.bat internal-bridge`
writes to `tools/ACRPacenoteTrace/bin/internal-bridge`, never the source tree.
