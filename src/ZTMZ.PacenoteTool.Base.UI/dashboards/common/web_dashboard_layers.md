# Web Dashboard Static Layers

Lua dashboards can retain artwork that does not change with telemetry in a WebDashboard canvas layer. Native overlays continue to render the same script normally.

```lua
if args.IsWebDashboard then
    gfx.BeginStaticLayer()
end

if not args.IsWebDashboard or args.WebDashboardStaticLayerDirty then
    drawStaticFrames(gfx, args.Self, args.GameData)
end

if args.IsWebDashboard then
    gfx.EndStaticLayer()
end
```

Use this only for artwork that depends on dashboard settings or resources, not live game data. `WebDashboardStaticLayerDirty` becomes true on the first web frame and after the dashboard settings are saved. Keep the static block before its dynamic commands and leave no transform, clip, or saved graphics state open at the end of the block. Dashboards without these markers stay on the complete-frame compatibility path.
