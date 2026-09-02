/**
 * ZTMZ Pacenote Dashboard - Web Client
 * Receives drawing commands from the server and renders them on canvas.
 * Compatible dashboards can instead execute their Lua package locally from a
 * compact telemetry stream; the server-rendered command path remains available.
 * VERSION: 2026-07-19-browser-lua
 */

console.log('Dashboard Client VERSION: 2026-07-19-browser-lua');

// ============================================================================
// Global State
// ============================================================================
const state = {
    ws: null,
    dashboards: [],
    dashboardPackages: new Map(),
    dashboardPackageLoads: new Map(),
    imageCache: {},
    imageLoads: {},
    isRunning: false,
    layerStack: null,
    layers: new Map(),
    renderWidth: 1920,
    renderHeight: 1080,
    canvasWidth: 1280,
    canvasHeight: 720,
    renderMode: 'legacy',
    sceneRevision: 0,
    orientation: 'landscape',
    controlsTimer: null,
    controlsFadeTimer: null,
    connectionState: 'connecting',
    receivedFrameCount: 0,
    frameRateWindowStartedAt: 0,
    pendingFrameData: null,
    pendingSnapshotData: null,
    renderFrameScheduled: false,
    pendingBrowserLuaSnapshotData: null,
    pendingBrowserLuaFrameData: null,
    browserLuaRenderScheduled: false,
    browserLuaManager: null,
    browserLuaHybrid: false
};

const colorCssCache = new Map();
const DASHBOARD_LAYER_Z_INDEX_STRIDE = 1000;

// ============================================================================
// Initialization
// ============================================================================
window.addEventListener('DOMContentLoaded', async () => {
    updateLoadingText('Loading dashboard fonts...');
    await loadDashboardFonts();

    updateLoadingText('Initializing canvas...');
    showCanvas();
    initializeBrowserLuaDashboardManager();
    initializeDashboardControls();
    initializeDashboardIndicators();
    
    updateLoadingText('Fetching dashboard list...');
    await loadDashboardList();
    
    hideLoading();
    updateLoadingText('Connecting to server...');
    connectWebSocket();
});

// ============================================================================
// Dashboard Loading
// ============================================================================
async function loadDashboardList() {
    try {
        const response = await fetch('/api/dashboards');
        if (!response.ok) throw new Error('Failed to fetch dashboards');
        
        state.dashboards = await response.json();
        renderDashboardList();
        
        // Preload enabled dashboards. A newly enabled dashboard is loaded on
        // demand from its draw commands without requiring a browser refresh.
        await loadDashboardImages();
    } catch (error) {
        showError('Failed to load dashboard list: ' + error.message);
        console.error(error);
    }
}

function renderDashboardList() {
    const listContainer = document.getElementById('dashboard-list');
    listContainer.innerHTML = '';
    
    const webEnabledCount = state.dashboards.filter(d => d.webEnabled ?? d.enabled).length;
    const card = document.createElement('div');
    card.className = 'dashboard-card';
    card.innerHTML = `
        <h3>Web Dashboard View</h3>
        <p><strong>${webEnabledCount} dashboard(s) routed to Web</strong></p>
        <p>Live dashboard data is streamed to your browser.</p>
        <p>Configured dashboards render automatically.</p>
    `;
    listContainer.appendChild(card);
}

async function loadDashboardImages() {
    for (const dashboardInfo of state.dashboards.filter(d => d.webEnabled ?? d.enabled)) {
        try {
            const dashboard = await fetchDashboardPackage(dashboardInfo.id);
            
            // Preload all images
            const imagePromises = Object.entries(dashboard.imageResources || {})
                .map(([key, url]) => loadImageResource(key, url));
            
            await Promise.all(imagePromises);
        } catch (error) {
            console.error(`Failed to load images for dashboard ${dashboardInfo.name}:`, error);
        }
    }
}

async function fetchDashboardPackage(id) {
    const dashboardId = Number(id);
    if (!Number.isInteger(dashboardId) || dashboardId < 0) {
        throw new Error('Invalid dashboard package identifier.');
    }

    const cachedPackage = state.dashboardPackages.get(dashboardId);
    if (cachedPackage) {
        return cachedPackage;
    }

    const pendingLoad = state.dashboardPackageLoads.get(dashboardId);
    if (pendingLoad) {
        return pendingLoad;
    }

    const load = fetch(`/api/dashboard/${dashboardId}`)
        .then(response => {
            if (!response.ok) {
                throw new Error(`Failed to fetch dashboard package ${dashboardId}.`);
            }
            return response.json();
        })
        .then(dashboardPackage => {
            state.dashboardPackages.set(dashboardId, dashboardPackage);
            return dashboardPackage;
        })
        .finally(() => state.dashboardPackageLoads.delete(dashboardId));
    state.dashboardPackageLoads.set(dashboardId, load);
    return load;
}

function loadImageResource(key, url) {
    if (!key || !url) {
        return Promise.resolve(null);
    }

    if (state.imageCache[key]) {
        return Promise.resolve(state.imageCache[key]);
    }

    if (state.imageLoads[key]) {
        return state.imageLoads[key];
    }

    const imageLoad = new Promise((resolve) => {
        const img = new Image();
        img.onload = () => {
            state.imageCache[key] = img;
            delete state.imageLoads[key];
            console.log('Loaded image:', key, 'from', url);
            requestImageLayerRefresh(key);
            resolve(img);
        };
        img.onerror = () => {
            delete state.imageLoads[key];
            console.warn(`Failed to load image: ${key} from ${url}`);
            resolve(null);
        };
        img.src = url;
    });

    state.imageLoads[key] = imageLoad;
    return imageLoad;
}

function initializeBrowserLuaDashboardManager() {
    const BrowserLuaDashboardManager = window.ZtmzBrowserLuaDashboardManager;
    if (typeof BrowserLuaDashboardManager !== 'function') {
        return;
    }

    state.browserLuaManager = new BrowserLuaDashboardManager({
        layerStack: () => state.layerStack,
        fetchPackage: fetchDashboardPackage,
        loadImage: loadImageResource,
        getImage: imageKey => state.imageCache[imageKey],
        requestScene: requestBrowserLuaScene,
        onFrameRendered: recordReceivedDashboardFrame,
        onError: error => {
            console.error('Browser Lua dashboard error:', error);
            showError('Dashboard renderer switched to compatibility mode.');
        },
        onFallback: sendBrowserLuaFallback
    });
}

// ============================================================================
// Drawing Command Player
// ============================================================================

/**
 * Execute a single drawing command on the canvas
 */
function executeDrawingCommand(ctx, canvas, cmd, layer) {
    
    try {
        switch (cmd.type) {
        case 'clear':
            if (cmd.color) {
                ctx.fillStyle = colorToCSS(cmd.color);
                ctx.fillRect(0, 0, state.renderWidth, state.renderHeight);
            } else {
                ctx.clearRect(0, 0, state.renderWidth, state.renderHeight);
            }
            break;
            
        case 'drawLine':
            ctx.save();
            ctx.strokeStyle = colorToCSS(cmd.color);
            ctx.lineWidth = cmd.thickness;
            ctx.beginPath();
            ctx.moveTo(cmd.x1, cmd.y1);
            ctx.lineTo(cmd.x2, cmd.y2);
            ctx.stroke();
            ctx.restore();
            break;
            
        case 'drawRectangle':
            ctx.save();
            ctx.strokeStyle = colorToCSS(cmd.color);
            ctx.lineWidth = cmd.thickness;
            ctx.strokeRect(cmd.x, cmd.y, cmd.width, cmd.height);
            ctx.restore();
            break;

        case 'drawRoundedRectangle':
            ctx.save();
            ctx.strokeStyle = colorToCSS(cmd.color);
            ctx.lineWidth = cmd.thickness;
            addRoundedRectanglePath(ctx, cmd.x, cmd.y, cmd.width, cmd.height, cmd.radius);
            ctx.stroke();
            ctx.restore();
            break;
            
        case 'fillRect':
        case 'fillRectangle':
            ctx.save();
            if (applyFillStyle(ctx, cmd)) {
                ctx.fillRect(cmd.x, cmd.y, cmd.width, cmd.height);
            }
            ctx.restore();
            break;

        case 'fillRoundedRectangle':
            ctx.save();
            if (applyFillStyle(ctx, cmd)) {
                addRoundedRectanglePath(ctx, cmd.x, cmd.y, cmd.width, cmd.height, cmd.radius);
                ctx.fill();
            }
            ctx.restore();
            break;
            
        case 'drawCircle':
            ctx.save();
            ctx.strokeStyle = colorToCSS(cmd.color);
            ctx.lineWidth = cmd.thickness;
            ctx.beginPath();
            ctx.arc(cmd.x, cmd.y, cmd.radius, 0, Math.PI * 2);
            ctx.stroke();
            ctx.restore();
            break;
            
        case 'fillCircle':
            ctx.save();
            if (applyFillStyle(ctx, cmd)) {
                ctx.beginPath();
                ctx.arc(cmd.x, cmd.y, cmd.radius, 0, Math.PI * 2);
                ctx.fill();
            }
            ctx.restore();
            break;

        case 'fillEllipse':
            ctx.save();
            if (applyFillStyle(ctx, cmd)) {
                ctx.beginPath();
                ctx.ellipse(cmd.x, cmd.y, cmd.radiusX, cmd.radiusY, 0, 0, Math.PI * 2);
                ctx.fill();
            }
            ctx.restore();
            break;
            
        case 'drawText':
        case 'drawTextWithBackground':
            drawTextCommand(ctx, cmd);
            break;
            
        case 'drawImage':
            const img = state.imageCache[cmd.imageKey];
            if (img) {
                ctx.save();
                ctx.globalAlpha = Number.isFinite(cmd.opacity)
                    ? Math.max(0, Math.min(1, cmd.opacity))
                    : 1;
                if (cmd.width && cmd.height) {
                    ctx.drawImage(img, cmd.x, cmd.y, cmd.width, cmd.height);
                } else {
                    ctx.drawImage(img, cmd.x, cmd.y);
                }
                ctx.restore();
            } else {
                loadImageResource(cmd.imageKey, cmd.imageUrl);
            }
            break;
            
        case 'drawImageRegion':
            const imgRegion = state.imageCache[cmd.imageKey];
            if (imgRegion) {
                ctx.drawImage(imgRegion, 
                    cmd.srcX, cmd.srcY, cmd.srcWidth, cmd.srcHeight,
                    cmd.destX, cmd.destY, cmd.destWidth, cmd.destHeight);
            } else {
                loadImageResource(cmd.imageKey, cmd.imageUrl);
            }
            break;
            
        case 'rotate':
            ctx.rotate(cmd.angle);
            break;
            
        case 'rotateAt':
            ctx.translate(cmd.centerX, cmd.centerY);
            ctx.rotate(cmd.angle);
            ctx.translate(-cmd.centerX, -cmd.centerY);
            break;
            
        case 'translate':
            ctx.translate(cmd.x, cmd.y);
            break;
            
        case 'scale':
            ctx.scale(cmd.x, cmd.y);
            break;
            
        case 'resetTransform':
            applyLogicalCanvasTransform(ctx, canvas);
            break;
            
        case 'save':
            ctx.save();
            layer.savedStateDepth++;
            break;
            
        case 'restore':
            restoreTrackedLayerState(layer);
            break;
            
        case 'setClip':
            ctx.save();
            layer.savedStateDepth++;
            ctx.beginPath();
            ctx.rect(cmd.x, cmd.y, cmd.width, cmd.height);
            ctx.clip();
            break;
            
        case 'releaseClip':
            restoreTrackedLayerState(layer);
            break;
        
        case 'fillGeometry':
            if (cmd.points && cmd.points.length > 0) {
                ctx.save();
                if (applyFillStyle(ctx, cmd)) {
                    ctx.beginPath();
                    // Points are flattened: [x1, y1, x2, y2, ...]
                    ctx.moveTo(cmd.points[0], cmd.points[1]);
                    for (let i = 2; i < cmd.points.length; i += 2) {
                        ctx.lineTo(cmd.points[i], cmd.points[i + 1]);
                    }
                    ctx.closePath();
                    ctx.fill();
                }
                ctx.restore();
            } else {
                console.warn('fillGeometry: No points or empty points array!');
            }
            break;
        
        case 'drawGeometry':
            if (cmd.points && cmd.points.length > 0) {
                ctx.save();
                ctx.strokeStyle = colorToCSS(cmd.color);
                ctx.lineWidth = cmd.thickness;
                ctx.beginPath();
                // Points are flattened: [x1, y1, x2, y2, ...]
                ctx.moveTo(cmd.points[0], cmd.points[1]);
                for (let i = 2; i < cmd.points.length; i += 2) {
                    ctx.lineTo(cmd.points[i], cmd.points[i + 1]);
                }
                ctx.closePath();
                ctx.stroke();
                ctx.restore();
            }
            break;
        
        default:
            console.warn('Unknown command type:', cmd.type, cmd);
            break;
        }
    } catch (error) {
        console.error('Error executing command:', cmd.type, cmd, error);
    }
}

async function loadDashboardFonts() {
    if (!document.fonts || !document.fonts.load) {
        return;
    }

    const fonts = [
        '400 14px "WRC Clean Roman"',
        '700 14px "WRC Clean Roman"',
        '400 14px "WRC Clean"',
        '700 14px "WRC Clean"',
        '400 14px "WRC Clean Bold"',
        '700 14px "WRC Clean Bold"'
    ];
    await Promise.all(fonts.map(font => document.fonts.load(font).catch(() => [])));
    await document.fonts.ready;
}

function drawTextCommand(ctx, cmd) {
    const fontSize = positiveNumber(cmd.fontSize, 14);
    const text = String(cmd.text ?? '');
    if (!text) {
        return;
    }

    ctx.save();
    ctx.font = getCanvasFont(cmd, fontSize);
    ctx.textBaseline = 'alphabetic';

    const lines = text.split('\n');
    const fontMetrics = ctx.measureText('Mg');
    const fallbackAscent = positiveNumber(
        fontMetrics.fontBoundingBoxAscent,
        positiveNumber(fontMetrics.actualBoundingBoxAscent, fontSize * 0.8));
    const fallbackDescent = positiveNumber(
        fontMetrics.fontBoundingBoxDescent,
        positiveNumber(fontMetrics.actualBoundingBoxDescent, fontSize * 0.2));
    const lineHeight = positiveNumber(cmd.lineHeight, fallbackAscent + fallbackDescent);
    const baseline = positiveNumber(cmd.baseline, fallbackAscent);

    let browserWidth = 0;
    for (const line of lines) {
        browserWidth = Math.max(browserWidth, ctx.measureText(line).width);
    }

    const textWidth = positiveNumber(cmd.textWidth, browserWidth);
    const textHeight = positiveNumber(cmd.textHeight, lines.length * lineHeight);

    if (cmd.type === 'drawTextWithBackground' && ((cmd.bgColor >>> 24) & 0xFF) > 0) {
        const padding = fontSize * 0.25;
        ctx.fillStyle = colorToCSS(cmd.bgColor);
        ctx.fillRect(
            cmd.x - padding,
            cmd.y - padding,
            textWidth + (padding * 2),
            textHeight + (padding * 2));
    }

    ctx.fillStyle = colorToCSS(cmd.color);
    for (let index = 0; index < lines.length; index++) {
        ctx.fillText(lines[index], cmd.x, cmd.y + baseline + (index * lineHeight));
    }

    ctx.restore();
}

function getCanvasFont(cmd, fontSize) {
    const weight = Number.isFinite(cmd.fontWeight)
        ? Math.max(100, Math.min(900, Math.round(cmd.fontWeight)))
        : 400;
    const style = cmd.fontStyle === 'italic' ? 'italic' : 'normal';
    const fontName = String(cmd.fontName || 'Arial')
        .replace(/\\/g, '\\\\')
        .replace(/"/g, '\\"');
    return `${style} ${weight} ${fontSize}px "${fontName}", Arial, sans-serif`;
}

function positiveNumber(value, fallback) {
    return Number.isFinite(value) && value > 0 ? value : fallback;
}

function applyFillStyle(ctx, cmd) {
    const alpha = (cmd.color >> 24) & 0xFF;
    if (cmd.gradient) {
        let gradient;
        if (cmd.gradient.type === 'linear') {
            gradient = ctx.createLinearGradient(
                cmd.gradient.x0, cmd.gradient.y0,
                cmd.gradient.x1, cmd.gradient.y1
            );
        } else if (cmd.gradient.type === 'radial') {
            gradient = ctx.createRadialGradient(
                cmd.gradient.x0, cmd.gradient.y0, cmd.gradient.r0,
                cmd.gradient.x1, cmd.gradient.y1, cmd.gradient.r1
            );
        }

        if (gradient && cmd.gradient.colors && cmd.gradient.colors.length > 0) {
            const colorCount = cmd.gradient.colors.length;
            for (let i = 0; i < colorCount; i++) {
                const position = colorCount > 1 ? i / (colorCount - 1) : 0;
                gradient.addColorStop(position, colorToCSS(cmd.gradient.colors[i]));
            }
            ctx.fillStyle = gradient;
            return true;
        }
    }

    if (alpha === 0) {
        return false;
    }

    ctx.fillStyle = colorToCSS(cmd.color);
    return true;
}

function addRoundedRectanglePath(ctx, x, y, width, height, radius) {
    const safeRadius = Math.max(0, Math.min(radius || 0, Math.abs(width) / 2, Math.abs(height) / 2));
    ctx.beginPath();
    ctx.moveTo(x + safeRadius, y);
    ctx.lineTo(x + width - safeRadius, y);
    ctx.quadraticCurveTo(x + width, y, x + width, y + safeRadius);
    ctx.lineTo(x + width, y + height - safeRadius);
    ctx.quadraticCurveTo(x + width, y + height, x + width - safeRadius, y + height);
    ctx.lineTo(x + safeRadius, y + height);
    ctx.quadraticCurveTo(x, y + height, x, y + height - safeRadius);
    ctx.lineTo(x, y + safeRadius);
    ctx.quadraticCurveTo(x, y, x + safeRadius, y);
    ctx.closePath();
}

function colorToCSS(color) {
    const cacheKey = color >>> 0;
    const cachedColor = colorCssCache.get(cacheKey);
    if (cachedColor !== undefined) {
        return cachedColor;
    }

    // Convert integer ARGB to CSS rgba
    const a = ((color >> 24) & 0xFF) / 255;
    const r = (color >> 16) & 0xFF;
    const g = (color >> 8) & 0xFF;
    const b = color & 0xFF;
    const cssColor = `rgba(${r}, ${g}, ${b}, ${a})`;

    if (colorCssCache.size < 256) {
        colorCssCache.set(cacheKey, cssColor);
    }

    return cssColor;
}

// ============================================================================
// WebSocket Connection
// ============================================================================
function connectWebSocket() {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${window.location.host}/ws`;
    
    updateConnectionStatus('Connecting...', 'connecting');
    
    state.ws = new WebSocket(wsUrl);
    
    state.ws.onopen = () => {
        console.log('WebSocket connected');
        updateConnectionStatus('Connected', 'connected');
        resetDashboardFrameRate();
        state.isRunning = true;
        announceBrowserLuaCapability();
    };
    
    state.ws.onmessage = (event) => {
        const isBrowserLuaTelemetry = typeof event.data === 'string'
            && event.data.includes('"type":"browserLuaTelemetry"');
        if (isBrowserLuaTelemetry) {
            queueBrowserLuaTelemetry(event.data);
            return;
        }

        const isLayeredFrame = typeof event.data === 'string'
            && event.data.includes('"type":"layeredDrawCommands"');
        const isLayeredSnapshot = isLayeredFrame
            && event.data.includes('"layers":');
        const isLegacyFrame = typeof event.data === 'string'
            && event.data.includes('"type":"drawCommands"');
        const isHybridCommandFrame = isLayeredFrame
            && event.data.includes('"browserLuaDashboardIds":[')
            && !event.data.includes('"browserLuaDashboardIds":[]');

        if (isHybridCommandFrame) {
            state.browserLuaHybrid = true;
        } else {
            // A normal server command frame means the server selected the
            // compatibility renderer. Stop local Lua immediately so a scheduled
            // browser frame cannot paint after the command frame has arrived.
            state.browserLuaHybrid = false;
            clearPendingBrowserLuaTelemetry();
            state.browserLuaManager?.suspend();
        }

        if (isLayeredSnapshot) {
            state.pendingSnapshotData = event.data;
        } else {
            state.pendingFrameData = event.data;
            if (isLegacyFrame) {
                state.pendingSnapshotData = null;
            }
        }

        if (!state.renderFrameScheduled) {
            state.renderFrameScheduled = true;
            requestAnimationFrame(renderLatestDashboardFrame);
        }
    };
    
    state.ws.onerror = (error) => {
        console.error('WebSocket error:', error);
        updateConnectionStatus('Connection error', 'disconnected');
    };
    
    state.ws.onclose = (event) => {
        console.warn('WebSocket closed:', event.code, event.reason || '(no reason)');
        updateConnectionStatus('Disconnected', 'disconnected');
        resetDashboardFrameRate();
        state.isRunning = false;
        state.browserLuaHybrid = false;
        clearPendingBrowserLuaTelemetry();
        state.browserLuaManager?.suspend();
        
        // Auto-reconnect after 3 seconds
        setTimeout(() => {
            if (!state.ws || state.ws.readyState === WebSocket.CLOSED) {
                connectWebSocket();
            }
        }, 3000);
    };
}

function announceBrowserLuaCapability() {
    if (!state.ws || state.ws.readyState !== WebSocket.OPEN) {
        return;
    }

    state.ws.send(JSON.stringify({
        type: 'browserLuaCapabilities',
        supported: Boolean(state.browserLuaManager?.canAdvertiseSupport)
    }));
}

function requestBrowserLuaScene() {
    if (!state.ws || state.ws.readyState !== WebSocket.OPEN) {
        return;
    }

    state.ws.send(JSON.stringify({ type: 'requestBrowserLuaScene' }));
}

function sendBrowserLuaFallback(reason) {
    clearPendingBrowserLuaTelemetry();
    if (!state.ws || state.ws.readyState !== WebSocket.OPEN) {
        return;
    }

    console.warn('Requesting browser Lua compatibility fallback:', reason);
    state.ws.send(JSON.stringify({
        type: 'browserLuaFallback',
        reason: String(reason || '').slice(0, 256)
    }));
}

function queueBrowserLuaTelemetry(serializedMessage) {
    const isSceneSnapshot = serializedMessage.includes('"dashboards":');
    if (isSceneSnapshot) {
        // A snapshot supersedes any delta from the preceding scene revision.
        state.pendingBrowserLuaSnapshotData = serializedMessage;
        state.pendingBrowserLuaFrameData = null;
    } else {
        state.pendingBrowserLuaFrameData = serializedMessage;
    }

    if (!state.browserLuaRenderScheduled) {
        state.browserLuaRenderScheduled = true;
        requestAnimationFrame(renderLatestBrowserLuaTelemetry);
    }
}

function renderLatestBrowserLuaTelemetry() {
    state.browserLuaRenderScheduled = false;
    const snapshotData = state.pendingBrowserLuaSnapshotData;
    const frameData = state.pendingBrowserLuaFrameData;
    state.pendingBrowserLuaSnapshotData = null;
    state.pendingBrowserLuaFrameData = null;

    if (snapshotData) {
        handleBrowserLuaTelemetry(snapshotData);
    }
    if (frameData) {
        handleBrowserLuaTelemetry(frameData);
    }
}

function clearPendingBrowserLuaTelemetry() {
    state.pendingBrowserLuaSnapshotData = null;
    state.pendingBrowserLuaFrameData = null;
}

function handleBrowserLuaTelemetry(serializedMessage) {
    if (!state.browserLuaManager) {
        sendBrowserLuaFallback('The browser Lua runtime is unavailable.');
        return;
    }

    try {
        const message = JSON.parse(serializedMessage);
        const wasHybrid = state.browserLuaHybrid;
        state.browserLuaHybrid = message.hybrid === true;
        if (Array.isArray(message.dashboards)) {
            if (!state.browserLuaHybrid) {
                // Discard queued compatibility frames before enabling a pure
                // browser-Lua scene. In hybrid mode they are the other dashboards.
                state.pendingSnapshotData = null;
                state.pendingFrameData = null;
                clearCommandRenderLayers();
            }
        } else if (wasHybrid && !state.browserLuaHybrid) {
            clearCommandRenderLayers();
        }
        state.browserLuaManager.handleTelemetry(message);
    } catch (error) {
        console.error('Browser Lua telemetry message error:', error);
        sendBrowserLuaFallback(error instanceof Error ? error.message : String(error));
    }
}

function renderLatestDashboardFrame() {
    state.renderFrameScheduled = false;
    const snapshotData = state.pendingSnapshotData;
    const frameData = state.pendingFrameData;
    state.pendingSnapshotData = null;
    state.pendingFrameData = null;

    if (!snapshotData && !frameData) {
        return;
    }

    // The latest complete state wins. This coalesces high-rate WebSocket messages
    // into one browser paint while preserving retained static canvas layers.
    try {
        if (snapshotData) {
            renderDashboardMessage(JSON.parse(snapshotData));
        }
        if (frameData) {
            renderDashboardMessage(JSON.parse(frameData));
        }
        if (!state.browserLuaHybrid) {
            recordReceivedDashboardFrame();
        }
    } catch (error) {
        console.error('WebSocket message error:', error);
    }
}

function renderDashboardMessage(message) {
    applyRenderResolution(message.canvasWidth, message.canvasHeight);

    switch (message.type) {
    case 'drawCommands':
        state.browserLuaHybrid = false;
        state.browserLuaManager?.suspend();
        renderLegacyDrawCommands(message);
        break;
    case 'layeredDrawCommands':
        state.browserLuaHybrid = hasBrowserLuaDashboardLayers(message);
        if (!state.browserLuaHybrid) {
            state.browserLuaManager?.suspend();
        }
        renderLayeredDrawCommands(message);
        break;
    }
}

function hasBrowserLuaDashboardLayers(message) {
    return Array.isArray(message?.browserLuaDashboardIds)
        && message.browserLuaDashboardIds.some(id => Number.isInteger(Number(id)) && Number(id) >= 0);
}

function renderLegacyDrawCommands(message) {
    const commands = Array.isArray(message.commands) ? message.commands : [];
    const layer = resetToLegacyLayer();
    layer.commands = commands;
    redrawLayer(layer);
}

function renderLayeredDrawCommands(message) {
    const sceneRevision = Number(message.sceneRevision);
    if (!Number.isFinite(sceneRevision) || sceneRevision <= 0) {
        return;
    }

    if (Array.isArray(message.layers)) {
        if (sceneRevision < state.sceneRevision) {
            return;
        }

        applyLayerSnapshot(sceneRevision, message.layers);
        return;
    }

    if (sceneRevision !== state.sceneRevision || state.renderMode !== 'layered') {
        requestLayeredSceneSnapshot();
        return;
    }

    const dynamicLayers = Array.isArray(message.dynamicLayers) ? message.dynamicLayers : [];
    for (const descriptor of dynamicLayers) {
        const id = typeof descriptor.id === 'string' ? descriptor.id : '';
        const layer = state.layers.get(id);
        if (!layer || layer.kind !== 'dynamic') {
            requestLayeredSceneSnapshot();
            return;
        }

        layer.commands = Array.isArray(descriptor.commands) ? descriptor.commands : [];
        redrawLayer(layer);
    }
}

function applyLayerSnapshot(sceneRevision, descriptors) {
    const nextLayerIds = new Set();
    const orderedLayers = [];

    for (const descriptor of descriptors) {
        const id = typeof descriptor.id === 'string' ? descriptor.id : '';
        const kind = descriptor.kind === 'static' ? 'static' : 'dynamic';
        if (!id || nextLayerIds.has(id)) {
            continue;
        }

        let layer = state.layers.get(id);
        if (!layer || layer.kind !== kind) {
            if (layer) {
                layer.canvas.remove();
            }
            layer = createRenderLayer(id, kind);
            state.layers.set(id, layer);
        }

        layer.commands = Array.isArray(descriptor.commands) ? descriptor.commands : [];
        redrawLayer(layer);
        orderedLayers.push(layer);
        nextLayerIds.add(id);
    }

    for (const [id, layer] of state.layers) {
        if (!nextLayerIds.has(id)) {
            layer.canvas.remove();
            state.layers.delete(id);
        }
    }

    for (const layer of orderedLayers) {
        // Appending an existing node moves it, preserving the server's original draw order.
        state.layerStack.appendChild(layer.canvas);
    }

    state.renderMode = 'layered';
    state.sceneRevision = sceneRevision;
}

function resetToLegacyLayer() {
    const legacyId = 'legacy:full';
    let legacyLayer = state.layers.get(legacyId);

    for (const [id, layer] of state.layers) {
        if (id !== legacyId) {
            layer.canvas.remove();
            state.layers.delete(id);
        }
    }

    if (!legacyLayer) {
        legacyLayer = createRenderLayer(legacyId, 'legacy');
        state.layers.set(legacyId, legacyLayer);
    }

    state.layerStack.appendChild(legacyLayer.canvas);
    state.renderMode = 'legacy';
    state.sceneRevision = 0;
    return legacyLayer;
}

function createRenderLayer(id, kind) {
    const canvas = document.createElement('canvas');
    canvas.className = 'dashboard-render-layer';
    canvas.width = state.canvasWidth;
    canvas.height = state.canvasHeight;
    canvas.dataset.dashboardLayer = id;
    canvas.style.zIndex = String(getDashboardLayerZIndex(id));
    const layer = {
        id,
        kind,
        canvas,
        ctx: canvas.getContext('2d'),
        commands: [],
        baselineSaved: false,
        savedStateDepth: 0
    };
    initializeLayerContext(layer);
    return layer;
}

function getDashboardLayerZIndex(id) {
    if (typeof id !== 'string') {
        return 0;
    }

    if (id.startsWith('scene:')) {
        return -DASHBOARD_LAYER_Z_INDEX_STRIDE;
    }

    const match = /^dashboard-(\d+):(static|dynamic):(\d+)$/.exec(id);
    if (!match) {
        return 0;
    }

    const dashboardOrder = Number(match[1]);
    const segment = Number(match[3]);
    if (!Number.isInteger(dashboardOrder) || !Number.isInteger(segment)) {
        return 0;
    }

    return (dashboardOrder * DASHBOARD_LAYER_Z_INDEX_STRIDE) + segment;
}

function redrawLayer(layer) {
    resetLayerContext(layer);
    for (const command of layer.commands) {
        executeDrawingCommand(layer.ctx, layer.canvas, command, layer);
    }
}

function resetLayerContext(layer) {
    if (typeof layer.ctx.reset === 'function') {
        layer.ctx.reset();
        initializeLayerContext(layer);
        return;
    }

    while (layer.savedStateDepth > 0) {
        layer.ctx.restore();
        layer.savedStateDepth--;
    }
    if (layer.baselineSaved) {
        layer.ctx.restore();
    }

    layer.ctx.setTransform(1, 0, 0, 1, 0, 0);
    layer.ctx.clearRect(0, 0, layer.canvas.width, layer.canvas.height);
    initializeLayerContext(layer);
}

function initializeLayerContext(layer) {
    applyLogicalCanvasTransform(layer.ctx, layer.canvas);
    layer.ctx.beginPath();
    layer.ctx.save();
    layer.baselineSaved = true;
    layer.savedStateDepth = 0;
}

function applyLogicalCanvasTransform(ctx, canvas) {
    ctx.setTransform(
        canvas.width / state.renderWidth,
        0,
        0,
        canvas.height / state.renderHeight,
        0,
        0);
}

function restoreTrackedLayerState(layer) {
    if (layer.savedStateDepth <= 0) {
        return;
    }

    layer.ctx.restore();
    layer.savedStateDepth--;
}

function applyRenderResolution(width, height) {
    const nextWidth = Number(width);
    const nextHeight = Number(height);
    if (!Number.isInteger(nextWidth) || !Number.isInteger(nextHeight)
        || nextWidth <= 0 || nextHeight <= 0
        || (nextWidth === state.canvasWidth && nextHeight === state.canvasHeight)) {
        return;
    }

    state.canvasWidth = nextWidth;
    state.canvasHeight = nextHeight;
    for (const layer of state.layers.values()) {
        layer.canvas.width = nextWidth;
        layer.canvas.height = nextHeight;
        layer.ctx = layer.canvas.getContext('2d');
        layer.baselineSaved = false;
        layer.savedStateDepth = 0;
        initializeLayerContext(layer);
        if (layer.commands.length > 0) {
            redrawLayer(layer);
        }
    }
}

function requestLayeredSceneSnapshot() {
    if (!state.ws || state.ws.readyState !== WebSocket.OPEN) {
        return;
    }

    state.ws.send(JSON.stringify({ type: 'requestSceneSnapshot' }));
}

function requestImageLayerRefresh(imageKey) {
    state.browserLuaManager?.requestImageRefresh(imageKey);
    requestAnimationFrame(() => {
        for (const layer of state.layers.values()) {
            if (layer.commands.some(command => command.imageKey === imageKey)) {
                redrawLayer(layer);
            }
        }
    });
}

function clearCommandRenderLayers() {
    for (const layer of state.layers.values()) {
        layer.canvas.remove();
    }
    state.layers.clear();
    state.renderMode = 'browser-lua';
    state.sceneRevision = 0;
}

// ============================================================================
// UI Controls
// ============================================================================
function showLoading() {
    document.getElementById('loading').style.display = 'flex';
}

function hideLoading() {
    document.getElementById('loading').style.display = 'none';
}

function updateLoadingText(text) {
    document.getElementById('loading-text').textContent = text;
}

function showDashboardSelector() {
    document.getElementById('dashboard-selector').classList.add('active');
    document.getElementById('menu-button').classList.remove('active');
    document.getElementById('connection-status').classList.remove('active');
    document.getElementById('frame-rate').classList.remove('active');
}

function hideDashboardSelector() {
    document.getElementById('dashboard-selector').classList.remove('active');
}

function showCanvas() {
    const container = document.getElementById('canvas-container');
    const layerStack = document.getElementById('dashboard-layer-stack');
    
    // Keep the server drawing coordinate system fixed at 1920x1080.
    state.layerStack = layerStack;
    state.layerStack.style.width = `${state.renderWidth}px`;
    state.layerStack.style.height = `${state.renderHeight}px`;
    
    container.classList.add('active');
    applyCanvasLayout();
}

function initializeDashboardControls() {
    const container = document.getElementById('canvas-container');
    const controls = document.getElementById('dashboard-controls');
    const fullscreenButton = document.getElementById('fullscreen-button');
    const orientationButton = document.getElementById('orientation-button');

    container.addEventListener('pointerup', (event) => {
        if (event.target.closest('#dashboard-controls')) {
            return;
        }

        showDashboardControls();
    });

    fullscreenButton.addEventListener('click', async (event) => {
        event.stopPropagation();
        await toggleFullscreen();
        showDashboardControls();
    });

    orientationButton.addEventListener('click', (event) => {
        event.stopPropagation();
        state.orientation = state.orientation === 'landscape' ? 'portrait' : 'landscape';
        applyCanvasLayout();
        showDashboardControls();
    });

    document.addEventListener('fullscreenchange', updateFullscreenControl);
    document.addEventListener('webkitfullscreenchange', updateFullscreenControl);
    controls.setAttribute('aria-hidden', 'true');
    updateFullscreenControl();
    updateOrientationControl();
}

function showDashboardControls() {
    const controls = document.getElementById('dashboard-controls');
    const frameRate = document.getElementById('frame-rate');
    const connectionStatus = document.getElementById('connection-status');
    controls.classList.add('active');
    controls.setAttribute('aria-hidden', 'false');
    frameRate.classList.add('active');
    connectionStatus.classList.add('active');

    window.clearTimeout(state.controlsTimer);
    window.clearTimeout(state.controlsFadeTimer);
    state.controlsTimer = window.setTimeout(() => {
        hideDashboardControls();
        state.controlsFadeTimer = window.setTimeout(() => {
            if (!controls.classList.contains('active')) {
                controls.setAttribute('aria-hidden', 'true');
            }
        }, 250);
    }, 4500);
}

function hideDashboardControls() {
    document.getElementById('dashboard-controls').classList.remove('active');
    document.getElementById('frame-rate').classList.remove('active');

    if (state.connectionState === 'connected') {
        document.getElementById('connection-status').classList.remove('active');
    }
}

async function toggleFullscreen() {
    const fullscreenElement = document.fullscreenElement || document.webkitFullscreenElement;
    const root = document.documentElement;

    try {
        if (fullscreenElement) {
            const exitFullscreen = document.exitFullscreen || document.webkitExitFullscreen;
            if (exitFullscreen) {
                await exitFullscreen.call(document);
            }
        } else {
            const requestFullscreen = root.requestFullscreen || root.webkitRequestFullscreen;
            if (!requestFullscreen) {
                showError('Fullscreen is not available in this browser.');
                return;
            }
            await requestFullscreen.call(root);
        }
    } catch (error) {
        console.error('Fullscreen toggle failed:', error);
        showError('Fullscreen could not be changed.');
    }
}

function updateFullscreenControl() {
    const button = document.getElementById('fullscreen-button');
    const isFullscreen = Boolean(document.fullscreenElement || document.webkitFullscreenElement);
    button.innerHTML = isFullscreen ? '&#x2922;' : '&#x26F6;';
    button.title = isFullscreen ? 'Exit fullscreen' : 'Enter fullscreen';
    button.setAttribute('aria-label', button.title);
}

function applyCanvasLayout() {
    if (!state.layerStack) {
        return;
    }

    const renderWidth = state.renderWidth;
    const renderHeight = state.renderHeight;
    const isPortrait = state.orientation === 'portrait';
    const layoutWidth = isPortrait ? renderHeight : renderWidth;
    const layoutHeight = isPortrait ? renderWidth : renderHeight;
    const scale = Math.min(window.innerWidth / layoutWidth, window.innerHeight / layoutHeight);

    state.layerStack.style.width = `${Math.max(1, Math.floor(renderWidth * scale))}px`;
    state.layerStack.style.height = `${Math.max(1, Math.floor(renderHeight * scale))}px`;
    state.layerStack.style.transform = isPortrait ? 'rotate(90deg)' : 'none';
    state.layerStack.style.transformOrigin = 'center center';
    updateOrientationControl();
}

function updateOrientationControl() {
    const button = document.getElementById('orientation-button');
    const isPortrait = state.orientation === 'portrait';
    button.title = isPortrait ? 'Switch to landscape layout' : 'Switch to portrait layout';
    button.setAttribute('aria-label', button.title);
    button.setAttribute('aria-pressed', isPortrait ? 'true' : 'false');
}

function initializeDashboardIndicators() {
    resetDashboardFrameRate();
    window.setInterval(updateMeasuredFrameRate, 500);
}

function recordReceivedDashboardFrame() {
    state.receivedFrameCount++;
    updateMeasuredFrameRate();
}

function resetDashboardFrameRate() {
    state.receivedFrameCount = 0;
    state.frameRateWindowStartedAt = performance.now();
    updateDashboardFrameRate(0);
}

function updateMeasuredFrameRate() {
    if (!state.frameRateWindowStartedAt) {
        resetDashboardFrameRate();
        return;
    }

    const elapsed = performance.now() - state.frameRateWindowStartedAt;
    if (elapsed < 1000) {
        return;
    }

    updateDashboardFrameRate(Math.round(state.receivedFrameCount * 1000 / elapsed));
    state.receivedFrameCount = 0;
    state.frameRateWindowStartedAt = performance.now();
}

function updateDashboardFrameRate(frameRate) {
    document.getElementById('frame-rate').textContent = `${frameRate} FPS`;
}

function updateConnectionStatus(text, connectionState) {
    const status = document.getElementById('connection-status');
    state.connectionState = connectionState;
    document.getElementById('connection-status-text').textContent = text;
    status.classList.remove('connecting', 'connected', 'disconnected');
    status.classList.add(connectionState);

    if (connectionState === 'connected') {
        showDashboardControls();
    } else {
        status.classList.add('active');
    }
}

function showError(message) {
    const errorDiv = document.getElementById('error-message');
    errorDiv.textContent = message;
    errorDiv.classList.add('active');
    
    setTimeout(() => {
        errorDiv.classList.remove('active');
    }, 5000);
}

// Handle window resize
window.addEventListener('resize', () => {
    applyCanvasLayout();
});

// Prevent context menu on long press (mobile)
window.addEventListener('contextmenu', (e) => e.preventDefault());
