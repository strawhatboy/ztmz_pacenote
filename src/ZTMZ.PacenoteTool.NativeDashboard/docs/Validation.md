# Native dashboard validation

## Completed on macOS

- CMake build with Apple Clang and address/undefined-behavior sanitizers.
- All five CTest suites passed: scene protocol, Lua runtime, 17 real built-in
  dashboard scripts/headless ImGui, backend contracts, and build configuration.
- Windows x64 Debug cross-build with llvm-mingw 20260922 / Clang 23.1.2. Compiled
  and linked DX9/10/11/12/Vulkan adapters, hooks, WinHTTP/WIC integration, viewer
  EXE, and overlay DLL. No Windows binary was executed.
- PE inspection confirmed x86-64 outputs and `ZtmzDashboardStart`/`Stop` exports.
- Vulkan adapter separately syntax-checked by Apple Clang using official Vulkan
  Headers v1.3.290.

The cross-build used pinned Lua/JSON/ImGui/MinHook sources already downloaded in
`/tmp`. Vulkan linking used an import library generated from the official
[Vulkan Loader v1.3.290 export definition](https://github.com/KhronosGroup/Vulkan-Loader/blob/v1.3.290/loader/vulkan-1.def).
This links against real loader export names; it does not supply or emulate a GPU.

## Reproduce a Windows cross-build

An unpacked llvm-mingw toolchain can be used through
`cmake/toolchains/llvm-mingw.cmake`:

```sh
cmake -S . -B out/windows-x64 \
  -DCMAKE_TOOLCHAIN_FILE=cmake/toolchains/llvm-mingw.cmake \
  -DZTMZ_LLVM_MINGW_ROOT=/path/to/llvm-mingw \
  -DBUILD_TESTING=OFF \
  '-DZTMZ_RENDER_BACKENDS=dx9;dx10;dx11;dx12;vulkan' \
  -DVulkan_INCLUDE_DIR=/path/to/Vulkan-Headers/include \
  -DVulkan_LIBRARY=/path/to/windows/vulkan-import-library.a
cmake --build out/windows-x64 --parallel
```

For native Windows builds, use the Visual Studio instructions in the README.
The llvm-mingw validation binaries depend on its `libc++.dll` and `libunwind.dll`,
Windows graphics/system libraries, and (when enabled) `vulkan-1.dll`. They are
validation artifacts, not a packaged distribution.

## Still required on Windows

Compile with MSVC and run each graphics path against a controlled application,
then selected games. Check transparent composition, images/colors, reset/resize,
Alt-Tab/fullscreen changes, device loss, other overlays, and start/stop. DX12 and
Vulkan tests must load the DLL before the objects required by their tracking
paths are created. Measure frame cost, especially Vulkan's synchronous fence
wait. Validate with the D3D debug layer and Vulkan validation layers in the
controlled applications.
