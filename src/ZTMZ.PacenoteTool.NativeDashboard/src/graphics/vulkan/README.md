# Vulkan glass

`VulkanGlass` copies the visible panels' padded union into a sampled GPU image,
before any HUD drawing, and uses a SPIR-V fragment shader for convex-edge
refraction, five-tap optional blur, tint and edge illumination. Instruments draw
afterward with ImGui's restored pipeline. The callback-only case is supported.

The ordinary swapchain must have `VK_IMAGE_USAGE_TRANSFER_SRC_BIT`. The hook
requests it for ordinary initial and replacement single-layer swapchains with
zero creation flags, after checking both surface usage support and implied image
format properties (format, usage, extent, layers and sample count). This preserves
glass across ordinary resolution changes. Unsupported requests are unchanged.
If augmented initial creation fails, the original initial request is retried.
Replacement failures propagate without retry: Vulkan retires `oldSwapchain` even
on failure. Retrying with that handle is invalid; retrying with NULL while the old
chain remains associated with the window must return `VK_ERROR_NATIVE_WINDOW_IN_USE_KHR`.
The hook never destroys a game-owned chain or changes its lifecycle to work around
this rule. Preflight cannot guarantee success under memory/device loss; such errors
are returned normally. See the primary [creation rules](https://docs.vulkan.org/refpages/latest/refpages/source/vkCreateSwapchainKHR.html)
and [retirement rules](https://docs.vulkan.org/refpages/latest/refpages/source/VkSwapchainCreateInfoKHR.html).

Known non-usage `pNext` entries permit inference from `imageUsage`. Image format
lists and Win32 fullscreen exclusive settings also permit augmentation. Other
pinned entries (device groups, counters, native HDR, present barriers/modes/scaling,
latency and compression) preserve existing overlay and explicit copy permissions,
but are not augmented without their extension-specific capability queries.
Unknown entries, including
the newer `VkImageUsageFlags2CreateInfoKHR` absent from pinned headers, leave the
creation request untouched and disable overlay access to that chain. This also
avoids inferring color-attachment permission from an overridden field. Existing
late-attachment/queue-ownership restrictions still apply. Known chains without
transfer-source usage, unsupported formats and allocation/pipeline failures use
the tinted glass fallback.

Capture is recorded only when a visible glass callback contributes a region.
It shares the backend's normal asynchronous command buffer and fence. Its submit
waits at both `TRANSFER` and `COLOR_ATTACHMENT_OUTPUT`, so the application must
finish rendering before the copy. Image barriers return the source to present
layout and make the snapshot readable by fragment shaders. There is no CPU
readback or queue-idle wait in the effect. Resources are replaced/released only
after the existing submission fence completes. `VulkanFrame::glassCapture`
reports copies and region dimensions; absent, clipped-out or unsupported glass
reports zero copies.

## Shader regeneration (developers only)

Normal builds use `vulkan_glass_shaders.hpp` and do not require an SDK or shader
compiler. The header was generated from `glass.hlsl` with Microsoft's official
DirectXShaderCompiler **v1.9.2609**, release asset `dxc_2026_09_29.zip`
(SHA256 `ad31b1fc8443175d204f77a611fdb3ef2ec42759bdc2f1167368de24a4a7e7f1`).
Download: https://github.com/microsoft/DirectXShaderCompiler/releases/tag/v1.9.2609

Set `ZTMZ_VULKAN_DXC` to a **SPIR-V-enabled** `dxc.exe` and build the optional
`regenerate_vulkan_glass_shaders` target. Some Windows SDK builds show `-spirv`
in help but reject compilation; the executable's presence is not proof of support.

If HLSL changed and configure reports a source-hash mismatch, regenerate directly
before configuring (paths below assume this directory is the current directory):

```text
dxc -spirv -fspv-target-env=vulkan1.0 -T vs_6_0 -E vsMain -O3 -Fo glass_vs.spv glass.hlsl
dxc -spirv -fspv-target-env=vulkan1.0 -T ps_6_0 -E psMain -O3 -Fo glass_ps.spv glass.hlsl
cmake -DOUTPUT=vulkan_glass_shaders.hpp -DSOURCE=glass.hlsl -Dvs=glass_vs.spv -Dps=glass_ps.spv -P embed_spirv.cmake
```

Generated SPIR-V binaries are temporary; commit the HLSL and generated header.
The generator and CMake normalize CRLF to LF before hashing, preventing checkout
line-ending differences from invalidating the header. SPIR-V words are emitted
as aligned `uint32_t` arrays. Compilation targets Vulkan 1.0.

## Verification

`vulkan_glass_tests` creates its own hidden Windows surface, real Vulkan
swapchain, checkerboard/gradient scene, semaphore chain and CPU readback buffer.
It verifies real pixel displacement and blur, unchanged foreground and outer
pixels, capture before other HUD commands, one bounded copy across multiple
panels, clipped/off/unsupported paths, visible fallback tint, and resize,
invalidation and teardown/restart. It enables Khronos validation when installed
and checks errors after teardown. Unavailable Vulkan/readback capabilities use
CTest skip code 77 rather than claiming success. Test-only GPU waits allow CPU
pixel comparisons; production glass never waits for the queue per frame.

Before GPU setup, injected creation-call tests exercise the production swapchain
policy: augmented replacement success, failed replacement retirement without any
retry, initial-request fallback with actual usage tracking, rejected surface and
image-format preflight, and unknown extensions behind a recognized `pNext` entry.
They do not deliberately send invalid creation requests to a driver.

The existing `vulkan_hook_lifecycle` test separately exercises actual hook
attachment, asynchronous uploads, presentation semaphores and swapchain lifecycle
inside its own test process. Neither test injects into a running game.
