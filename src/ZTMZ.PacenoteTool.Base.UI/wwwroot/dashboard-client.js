/**
 * ZTMZ Pacenote Dashboard - Web Client
 * Receives drawing commands from server and renders them on canvas
 * Server executes Lua dashboards and sends rendered commands
 * VERSION: 2025-10-19-v2 (transparent fix)
 */

console.log('Dashboard Client VERSION: 2025-10-19-v2 (transparent fix)');

// ============================================================================
// Global State
// ============================================================================
const state = {
    ws: null,
    dashboards: [],
    imageCache: {},
    isRunning: false,
    canvas: null,
    ctx: null,
    orientation: 'landscape',
    controlsTimer: null,
    controlsFadeTimer: null
};

// ============================================================================
// Initialization
// ============================================================================
window.addEventListener('DOMContentLoaded', async () => {
    updateLoadingText('Initializing canvas...');
    showCanvas();
    initializeDashboardControls();
    
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
        
        // Preload images for all enabled dashboards
        await loadDashboardImages();
    } catch (error) {
        showError('Failed to load dashboard list: ' + error.message);
        console.error(error);
    }
}

function renderDashboardList() {
    const listContainer = document.getElementById('dashboard-list');
    listContainer.innerHTML = '';
    
    const enabledCount = state.dashboards.filter(d => d.enabled).length;
    const card = document.createElement('div');
    card.className = 'dashboard-card';
    card.innerHTML = `
        <h3>Web Dashboard View</h3>
        <p><strong>${enabledCount} dashboard(s) enabled</strong></p>
        <p>Dashboards are rendered server-side and streamed to your browser.</p>
        <p>The server will automatically render all enabled dashboards.</p>
    `;
    listContainer.appendChild(card);
}

async function loadDashboardImages() {
    // Load all dashboard images for all enabled dashboards
    for (const dashboardInfo of state.dashboards.filter(d => d.enabled)) {
        try {
            const response = await fetch(`/api/dashboard/${dashboardInfo.id}`);
            if (!response.ok) continue;
            
            const dashboard = await response.json();
            
            // Preload all images
            const imagePromises = Object.entries(dashboard.imageResources).map(([key, url]) => {
                return new Promise((resolve) => {
                    const img = new Image();
                    img.onload = () => {
                        state.imageCache[key] = img;
                        console.log('Loaded image:', key, 'from', url);
                        resolve();
                    };
                    img.onerror = () => {
                        console.warn(`Failed to load image: ${key} from ${url}`);
                        resolve();
                    };
                    img.src = url;
                });
            });
            
            await Promise.all(imagePromises);
        } catch (error) {
            console.error(`Failed to load images for dashboard ${dashboardInfo.name}:`, error);
        }
    }
}

// ============================================================================
// Drawing Command Player
// ============================================================================

/**
 * Execute a single drawing command on the canvas
 */
function executeDrawingCommand(cmd) {
    const ctx = state.ctx;
    
    try {
        switch (cmd.type) {
        case 'clear':
            const clearAlpha = cmd.color ? (cmd.color >> 24) & 0xFF : 0;
            if (cmd.color) {
                ctx.fillStyle = colorToCSS(cmd.color);
                ctx.fillRect(0, 0, state.canvas.width, state.canvas.height);
            } else {
                ctx.clearRect(0, 0, state.canvas.width, state.canvas.height);
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
            ctx.fillStyle = colorToCSS(cmd.color);
            ctx.beginPath();
            ctx.arc(cmd.x, cmd.y, cmd.radius, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
            break;

        case 'fillEllipse':
            ctx.save();
            ctx.fillStyle = colorToCSS(cmd.color);
            ctx.beginPath();
            ctx.ellipse(cmd.x, cmd.y, cmd.radiusX, cmd.radiusY, 0, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
            break;
            
        case 'drawText':
            ctx.fillStyle = colorToCSS(cmd.color);
            
        case 'drawTextWithBackground':
            ctx.save();
            ctx.font = `${cmd.fontSize}px ${cmd.fontName}`;
            
            // Handle multi-line text
            const bgLines = cmd.text.split('\n');
            const lineHeight = cmd.fontSize * 1.2;
            
            // Calculate total text dimensions
            let maxWidth = 0;
            for (const line of bgLines) {
                const metrics = ctx.measureText(line);
                maxWidth = Math.max(maxWidth, metrics.width);
            }
            const totalHeight = bgLines.length * lineHeight;
            
            // Draw background (skip if fully transparent)
            const bgAlpha = ((cmd.bgColor >> 24) & 0xFF);
            if (bgAlpha > 0) {
                ctx.fillStyle = colorToCSS(cmd.bgColor);
                ctx.fillRect(cmd.x, cmd.y, maxWidth, totalHeight);
            }
            
            // Draw text lines
            ctx.fillStyle = colorToCSS(cmd.color);
            ctx.textBaseline = 'top';
            for (let i = 0; i < bgLines.length; i++) {
                ctx.fillText(bgLines[i], cmd.x, cmd.y + (i * lineHeight));
            }
            
            ctx.restore();
            break;
            
        case 'drawImage':
            const img = state.imageCache[cmd.imageKey];
            if (img) {
                if (cmd.width && cmd.height) {
                    ctx.drawImage(img, cmd.x, cmd.y, cmd.width, cmd.height);
                } else {
                    ctx.drawImage(img, cmd.x, cmd.y);
                }
            } else {
                console.warn('DrawImage: Image not found in cache:', cmd.imageKey);
            }
            break;
            
        case 'drawImageRegion':
            const imgRegion = state.imageCache[cmd.imageKey];
            if (imgRegion) {
                ctx.drawImage(imgRegion, 
                    cmd.srcX, cmd.srcY, cmd.srcWidth, cmd.srcHeight,
                    cmd.destX, cmd.destY, cmd.destWidth, cmd.destHeight);
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
            ctx.setTransform(1, 0, 0, 1, 0, 0);
            break;
            
        case 'save':
            ctx.save();
            break;
            
        case 'restore':
            ctx.restore();
            break;
            
        case 'setClip':
            ctx.save();
            ctx.beginPath();
            ctx.rect(cmd.x, cmd.y, cmd.width, cmd.height);
            ctx.clip();
            break;
            
        case 'releaseClip':
            ctx.restore();
            break;
        
        case 'fillGeometry':
            if (cmd.points && cmd.points.length > 0) {
                ctx.save();
                ctx.fillStyle = colorToCSS(cmd.color);
                ctx.beginPath();
                // Points are flattened: [x1, y1, x2, y2, ...]
                ctx.moveTo(cmd.points[0], cmd.points[1]);
                for (let i = 2; i < cmd.points.length; i += 2) {
                    ctx.lineTo(cmd.points[i], cmd.points[i + 1]);
                }
                // Don't call closePath() - the geometry is already closed by the arc points
                // ctx.closePath();  
                ctx.fill();
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
    // Convert integer ARGB to CSS rgba
    const a = ((color >> 24) & 0xFF) / 255;
    const r = (color >> 16) & 0xFF;
    const g = (color >> 8) & 0xFF;
    const b = color & 0xFF;
    return `rgba(${r}, ${g}, ${b}, ${a})`;
}

// ============================================================================
// WebSocket Connection
// ============================================================================
function connectWebSocket() {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${window.location.host}/ws`;
    
    updateConnectionStatus('Connecting...', false);
    
    state.ws = new WebSocket(wsUrl);
    
    state.ws.onopen = () => {
        console.log('WebSocket connected');
        updateConnectionStatus('Connected', true);
        state.isRunning = true;
        // No render loop needed - commands come via WebSocket messages
    };
    
    state.ws.onmessage = (event) => {
        try {
            const message = JSON.parse(event.data);
            
            if (message.type === 'drawCommands') {
                // Received drawing commands from server - render them immediately
                const commands = message.commands;
                
                for (const cmd of commands) {
                    executeDrawingCommand(cmd);
                }
            } else {
                // Ignore unknown message types
            }
        } catch (error) {
            console.error('WebSocket message error:', error);
        }
    };
    
    state.ws.onerror = (error) => {
        console.error('WebSocket error:', error);
        updateConnectionStatus('Error', false);
    };
    
    state.ws.onclose = () => {
        console.log('WebSocket closed');
        updateConnectionStatus('Disconnected', false);
        state.isRunning = false;
        
        // Auto-reconnect after 3 seconds
        setTimeout(() => {
            if (!state.ws || state.ws.readyState === WebSocket.CLOSED) {
                connectWebSocket();
            }
        }, 3000);
    };
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
}

function hideDashboardSelector() {
    document.getElementById('dashboard-selector').classList.remove('active');
}

function showCanvas() {
    const container = document.getElementById('canvas-container');
    const canvas = document.getElementById('dashboard-canvas');
    
    // Keep the server drawing coordinate system fixed at 1920x1080.
    canvas.width = 1920;
    canvas.height = 1080;
    state.canvas = canvas;
    state.ctx = canvas.getContext('2d');
    
    container.classList.add('active');
    document.getElementById('connection-status').classList.add('active');
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
    controls.classList.add('active');
    controls.setAttribute('aria-hidden', 'false');

    window.clearTimeout(state.controlsTimer);
    window.clearTimeout(state.controlsFadeTimer);
    state.controlsTimer = window.setTimeout(() => {
        controls.classList.remove('active');
        state.controlsFadeTimer = window.setTimeout(() => {
            if (!controls.classList.contains('active')) {
                controls.setAttribute('aria-hidden', 'true');
            }
        }, 250);
    }, 4500);
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
    if (!state.canvas) {
        return;
    }

    const renderWidth = state.canvas.width;
    const renderHeight = state.canvas.height;
    const isPortrait = state.orientation === 'portrait';
    const layoutWidth = isPortrait ? renderHeight : renderWidth;
    const layoutHeight = isPortrait ? renderWidth : renderHeight;
    const scale = Math.min(window.innerWidth / layoutWidth, window.innerHeight / layoutHeight);

    state.canvas.style.width = `${Math.max(1, Math.floor(renderWidth * scale))}px`;
    state.canvas.style.height = `${Math.max(1, Math.floor(renderHeight * scale))}px`;
    state.canvas.style.transform = isPortrait ? 'rotate(90deg)' : 'none';
    state.canvas.style.transformOrigin = 'center center';
    updateOrientationControl();
}

function updateOrientationControl() {
    const button = document.getElementById('orientation-button');
    const isPortrait = state.orientation === 'portrait';
    button.title = isPortrait ? 'Switch to landscape layout' : 'Switch to portrait layout';
    button.setAttribute('aria-label', button.title);
    button.setAttribute('aria-pressed', isPortrait ? 'true' : 'false');
}

function updateConnectionStatus(text, connected) {
    const status = document.getElementById('connection-status');
    status.textContent = text;
    status.className = connected ? 'active connected' : 'active disconnected';
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
