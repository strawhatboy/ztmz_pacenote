# Native Static Image Layers

Built-in native dashboards can cache image-only static artwork in a Direct2D-compatible bitmap while continuing to use the existing overlay window. Add `"SupportsNativeStaticImageCache": true` to `info.json` only when the dashboard's static block draws images and does not depend on live telemetry.

The native overlay calls the script once with `args.NativeStaticLayerRenderOnly` set. Render the static artwork through `args.NativeStaticLayerGraphics` and return. During normal native frames, skip that artwork when `args.NativeStaticLayerEnabled` is true.

The cache is rebuilt when dashboard settings, stage context, overlay size, or the Direct2D device changes. If any cache step fails, the dashboard automatically uses its normal full-frame rendering path.
