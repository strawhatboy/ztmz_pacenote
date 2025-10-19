# Web Dashboard Debugging Guide

## Current Status
The server-side rendering architecture is implemented, but the web client is not displaying anything.

## Debugging Features Added

### 1. Server-Side Logging (GameOverlayManager.cs)
```csharp
// Line ~478: Check if web dashboard server is active
_logger.Debug($"Captured {commands?.Count ?? 0} drawing commands for {_webDashboardServer.ClientCount} web clients");

// Line ~558: Log command capture results
_logger.Debug($"CaptureWebDashboardCommands returning {commands.Count} commands");
```

### 2. Server-Side Logging (WebDashboardServer.cs)
```csharp
// Line ~292: Log command broadcasting
_logger.Debug($"Broadcasting {commands.Count} drawing commands to {_connectedClients.Count} clients");
_logger.Debug($"JSON size: {commandsJson.Length} bytes");
```

### 3. Client-Side Logging (dashboard-client.js)
```javascript
// Line ~288: Log all WebSocket messages
console.log('WebSocket message received:', message.type, message);
console.log(`Executing ${commands.length} drawing commands`);
```

### 4. Test Commands
If no commands are captured from dashboards, the server will automatically send test commands:
- Black background (clear)
- Red rectangle at (100, 100) size 200x100
- White text: "TEST - Server is sending commands"

## How to Debug

### Step 1: Check Server Logs
Look for these log messages in the application console:

✅ **Server Started**:
```
WebDashboard server started on port 8080
```

✅ **Clients Connected**:
```
Captured X drawing commands for Y web clients
```

✅ **Commands Captured**:
```
CaptureWebDashboardCommands returning X commands
```

✅ **Commands Sent**:
```
Broadcasting X drawing commands to Y clients
JSON size: XXXX bytes
```

### Step 2: Check Browser Console
Open browser DevTools (F12) and look for:

✅ **WebSocket Connected**:
```
WebSocket connected
```

✅ **Messages Received**:
```
WebSocket message received: drawCommands {type: "drawCommands", commands: Array(X)}
Executing X drawing commands
```

✅ **No Errors**:
```
No "WebSocket message error" or "executeDrawingCommand" errors
```

### Step 3: Visual Check
- Black background should appear
- Red rectangle should be visible at top-left area
- White text should be readable

## Common Issues

### Issue: "No commands captured - adding test command" in logs
**Meaning**: Dashboards are not generating any drawing commands
**Possible Causes**:
1. No dashboards are enabled
2. Dashboards are not calling Graphics methods
3. GraphicsCommandProxy is not intercepting calls correctly

### Issue: No "Broadcasting" log messages
**Meaning**: Commands are not reaching the broadcast function
**Possible Causes**:
1. Web dashboard server not running (check `WebDashboardEnabled` in config)
2. No web clients connected (check browser is open to http://localhost:8080)
3. Exception in CaptureWebDashboardCommands (check error logs)

### Issue: Browser shows "WebSocket connected" but no "message received" logs
**Meaning**: Server is not sending messages
**Possible Causes**:
1. Game is not running (commands only sent during gameplay)
2. No enabled dashboards
3. Server-side exception (check logs)

### Issue: Messages received but nothing drawn
**Possible Causes**:
1. Canvas not initialized (check canvas size in DevTools)
2. Commands have wrong format (check command structure in console)
3. Colors are transparent (alpha = 0)
4. Drawing outside visible area

## Next Steps

1. **Build and run** the application
2. **Enable web dashboard** in settings
3. **Enable at least one dashboard**
4. **Open browser** to http://localhost:8080
5. **Open browser DevTools** (F12) → Console tab
6. **Check logs** in both application console and browser console
7. **Look for the test red rectangle** - if visible, command streaming works!

## Expected Flow

```
Game Updates (60 FPS)
    ↓
GameOverlayManager._window_DrawGraphics()
    ↓
CaptureWebDashboardCommands()
    ↓
Dashboard.Render(GraphicsCommandProxy)
    ↓
GraphicsCommandProxy → DrawingCommandCapture
    ↓
List<DrawingCommand>
    ↓
WebDashboardServer.BroadcastGameData(commands)
    ↓
JSON.serialize({type: "drawCommands", commands: [...]})
    ↓
WebSocket.send()
    ↓
Browser receives WebSocket message
    ↓
dashboard-client.js parses JSON
    ↓
executeDrawingCommand() for each command
    ↓
Canvas 2D API draws
    ↓
Visual output!
```

## Files Modified with Debug Code

- `GameOverlayManager.cs` - Added logging and test commands
- `WebDashboardServer.cs` - Added logging
- `dashboard-client.js` - Added logging

Remember to remove debug logging after fixing the issue!
