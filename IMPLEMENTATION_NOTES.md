# Server-Side Dashboard Rendering Implementation Notes

## Current Status

### ✅ Completed
1. **DrawingCommand.cs** - Complete command model system
2. **DrawingCommandCapture.cs** - Captures Graphics API calls
3. **dashboard-client.js** - Clean 400-line command player (removed 1200 lines of Fengari code)
4. **WebDashboardServer.cs** - Updated to accept and broadcast drawing commands

### 🔄 In Progress  
**The Challenge**: Capturing Graphics calls from Lua dashboards

## The Problem

The current architecture:
```
GameOverlayManager._window_DrawGraphics()
  └─> dashboard.Render(DashboardScriptArguments)
      └─> Lua script calls Graphics.DrawLine(), Graphics.FillRectangle(), etc.
          └─> These go directly to GameOverlay.Drawing.Graphics (sealed class)
```

We need to intercept those Graphics calls to capture them as DrawingCommand objects.

## Possible Solutions

### Option 1: Wrap Graphics Class (COMPLEX)
Create a `GraphicsCapture` wrapper that implements all Graphics methods:
- Pros: Clean, transparent
- Cons: Graphics may be sealed, 50+ methods to wrap

### Option 2: Modify Lua Scripts (NOT IDEAL)
Change all dashboard Lua scripts to use a wrapper API
- Pros: Simple to implement
- Cons: Breaks existing dashboards, maintenance burden

### Option 3: Separate Web Rendering Path (RECOMMENDED)
Create a parallel rendering system just for web clients:

```csharp
// In GameOverlayManager.cs
private DrawingCommandCapture _webCommandCapture = null;

private void _window_DrawGraphics(object sender, DrawGraphicsEventArgs e) {
    var gfx = e.Graphics;
    gfx.ClearScene(_brushes["clear"]);
    
    if (Config.Instance.UI_ShowHud && TimeToShowTelemetry) {
        // Render dashboards for game overlay (existing code)
        for (var i = 0; i < Dashboards.Count; i++) {
            // ... existing rendering ...
        }

        // Capture commands for web clients
        if (_webDashboardServer != null && _webDashboardServer.IsRunning && _webDashboardServer.ClientCount > 0)
        {
            var commands = CaptureWebDashboardCommands();
            _ = _webDashboardServer.BroadcastGameData(
                DashboardScriptArguments.GameData,
                DashboardScriptArguments.GameContext,
                commands
            );
        }
    }
}

private List<DrawingCommand> CaptureWebDashboardCommands()
{
    // Create a command capture instance
    if (_webCommandCapture == null)
    {
        _webCommandCapture = new DrawingCommandCapture
        {
            Width = _window.Width,
            Height = _window.Height
        };
    }
    
    // TODO: Render dashboards using DrawingCommandCapture instead of Graphics
    // This requires either:
    // A) Creating a mock Graphics object that forwards to DrawingCommandCapture
    // B) Modifying DashboardScriptArguments to use an interface instead of Graphics
    // C) Using reflection to intercept Graphics calls
    
    return _webCommandCapture.GetCommands();
}
```

### Option 4: Use GameOverlay Frame Capture (SIMPLEST)
Instead of command streaming, capture the game overlay window as images:

```csharp
private async Task BroadcastOverlayFrame()
{
    // Capture the overlay window as a bitmap
    var bitmap = CaptureOverlayWindow();
    var jpegBytes = ConvertToJpeg(bitmap, quality: 70);
    var base64 = Convert.ToBase64String(jpegBytes);
    
    await _webDashboardServer.BroadcastFrame(base64);
}
```

Then on the web client:
```javascript
if (message.type === 'frame') {
    const img = new Image();
    img.onload = () => ctx.drawImage(img, 0, 0);
    img.src = 'data:image/jpeg;base64,' + message.data;
}
```

## Recommendation

For **immediate functionality**, I recommend **Option 4** (Frame Capture):
- ✅ Works immediately with existing dashboards
- ✅ No Lua/Graphics refactoring needed
- ✅ ~100 lines of code
- ⚠️ Higher bandwidth (10-50KB per frame vs 1-5KB for commands)
- ⚠️ 30-60 FPS instead of full 144+ FPS

For **long-term quality**, implement **Option 3** (Separate Rendering Path):
- ✅ Low bandwidth
- ✅ Full frame rate
- ✅ Perfect quality
- ⚠️ Requires creating a Graphics wrapper/mock
- ⚠️ ~500 lines of code

## Next Steps

### To Complete Option 4 (Quick Win):
1. Add `System.Drawing` reference to capture window
2. Implement `CaptureOverlayWindow()` method
3. Change `BroadcastGameData` to `BroadcastFrame`
4. Update `dashboard-client.js` to handle frame messages
5. Test with real dashboards

### To Complete Option 3 (Proper Solution):
1. Create `IGraphics` interface with all Graphics methods
2. Create `GraphicsCaptureWrapper : IGraphics` that wraps DrawingCommandCapture
3. Modify `DashboardScriptArguments` to use `IGraphics` instead of `Graphics`
4. Create separate web-only dashboard instances
5. Test rendering parity between overlay and web

## Files Modified
- ✅ `DrawingCommand.cs` (NEW)
- ✅ `DrawingCommandCapture.cs` (NEW)
- ✅ `wwwroot/dashboard-client.js` (REWRITTEN - 1200 → 400 lines)
- ✅ `WebDashboardServer.cs` (MODIFIED - added command broadcasting)
- ⏳ `GameOverlayManager.cs` (PENDING - needs capture implementation)

## Testing Plan
1. Start application with web dashboard enabled
2. Open browser to `http://localhost:8080`
3. Verify WebSocket connection
4. Verify commands are being sent (check browser console)
5. Verify dashboards render correctly
6. Compare web rendering to game overlay
