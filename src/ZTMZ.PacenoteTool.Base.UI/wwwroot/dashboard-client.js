/**
 * ZTMZ Pacenote Dashboard - Web Client
 * Executes Lua dashboards client-side using Fengari
 */

// ============================================================================
// Global State
// ============================================================================
const state = {
    ws: null,
    dashboards: [],
    currentDashboard: null,
    luaEnv: null,
    gameData: null,
    gameContext: null,
    imageCache: {},
    fontCache: {},
    isRunning: false,
    lastFrameTime: 0,
    canvas: null,
    ctx: null
};

// ============================================================================
// Initialization
// ============================================================================
window.addEventListener('DOMContentLoaded', async () => {
    updateLoadingText('Loading Fengari Lua interpreter...');
    
    // Wait for Fengari to load
    await waitForFengari();
    
    updateLoadingText('Fetching dashboard list...');
    await loadDashboardList();
    
    hideLoading();
    showDashboardSelector();
});

function waitForFengari() {
    return new Promise((resolve) => {
        const check = () => {
            if (window.fengari) {
                resolve();
            } else {
                setTimeout(check, 100);
            }
        };
        check();
    });
}

// ============================================================================
// Dashboard Loading
// ============================================================================
async function loadDashboardList() {
    try {
        const response = await fetch('/api/dashboards');
        if (!response.ok) throw new Error('Failed to fetch dashboards');
        
        state.dashboards = await response.json();
        renderDashboardList();
    } catch (error) {
        showError('Failed to load dashboard list: ' + error.message);
        console.error(error);
    }
}

function renderDashboardList() {
    const listContainer = document.getElementById('dashboard-list');
    listContainer.innerHTML = '';
    
    state.dashboards.forEach((dashboard, index) => {
        const card = document.createElement('div');
        card.className = 'dashboard-card' + (dashboard.enabled ? '' : ' disabled');
        
        const html = `
            <h3>${dashboard.name}</h3>
            <p><strong>Author:</strong> ${dashboard.author || 'Unknown'}</p>
            <p><strong>Version:</strong> ${dashboard.version || 'N/A'}</p>
            <p>${dashboard.description || ''}</p>
            ${dashboard.previewImage ? `<img class="preview" src="${dashboard.previewImage}" alt="Preview">` : ''}
        `;
        card.innerHTML = html;
        
        if (dashboard.enabled) {
            card.addEventListener('click', () => selectDashboard(dashboard.id));
        }
        
        listContainer.appendChild(card);
    });
}

async function selectDashboard(id) {
    updateLoadingText('Loading dashboard...');
    showLoading();
    
    try {
        const response = await fetch(`/api/dashboard/${id}`);
        if (!response.ok) throw new Error('Failed to fetch dashboard');
        
        const dashboard = await response.json();
        
        updateLoadingText('Loading resources...');
        await loadDashboardResources(dashboard);
        
        updateLoadingText('Initializing Lua environment...');
        await initializeLuaEnvironment(dashboard);
        
        state.currentDashboard = dashboard;
        
        hideDashboardSelector();
        hideLoading();
        showCanvas();
        
        updateLoadingText('Connecting to server...');
        connectWebSocket();
        
    } catch (error) {
        hideLoading();
        showError('Failed to load dashboard: ' + error.message);
        console.error(error);
    }
}

async function loadDashboardResources(dashboard) {
    // Preload all images
    const imagePromises = Object.entries(dashboard.imageResources).map(([key, url]) => {
        return new Promise((resolve, reject) => {
            const img = new Image();
            img.onload = () => {
                state.imageCache[key] = img;
                resolve();
            };
            img.onerror = () => {
                console.warn(`Failed to load image: ${key} from ${url}`);
                resolve(); // Continue even if image fails
            };
            img.src = url;
        });
    });
    
    await Promise.all(imagePromises);
}

// ============================================================================
// Lua Environment Setup
// ============================================================================
async function initializeLuaEnvironment(dashboard) {
    const fengari = window.fengari;
    const lua = fengari.lua;
    const lauxlib = fengari.lauxlib;
    const lualib = fengari.lualib;
    
    // Create new Lua state
    const L = lauxlib.luaL_newstate();
    lualib.luaL_openlibs(L);
    
    // Create Graphics bridge
    const graphicsBridge = createGraphicsBridge();
    
    // Inject global variables
    injectLuaGlobals(L, graphicsBridge, dashboard);
    
    // Load common Lua scripts
    for (const commonScript of dashboard.commonScripts) {
        if (lauxlib.luaL_dostring(L, fengari.to_luastring(commonScript)) !== lua.LUA_OK) {
            const error = lua.lua_tojsstring(L, -1);
            console.error('Error loading common Lua script:', error);
            throw new Error('Lua common script error: ' + error);
        }
    }
    
    // Load main dashboard script
    if (lauxlib.luaL_dostring(L, fengari.to_luastring(dashboard.luaScript)) !== lua.LUA_OK) {
        const error = lua.lua_tojsstring(L, -1);
        console.error('Error loading dashboard Lua script:', error);
        throw new Error('Lua script error: ' + error);
    }
    
    state.luaEnv = L;
    state.graphicsBridge = graphicsBridge;
    
    // Call onInit if exists
    lua.lua_getglobal(L, fengari.to_luastring('onInit'));
    if (lua.lua_isfunction(L, -1)) {
        if (lua.lua_pcall(L, 0, 0, 0) !== lua.LUA_OK) {
            const error = lua.lua_tojsstring(L, -1);
            console.error('Error in onInit:', error);
        }
    } else {
        lua.lua_pop(L, 1);
    }
}

function injectLuaGlobals(L, graphicsBridge, dashboard) {
    const fengari = window.fengari;
    const lua = fengari.lua;
    const lauxlib = fengari.lauxlib;
    
    // Graphics object
    lua.lua_newtable(L);
    for (const [funcName, func] of Object.entries(graphicsBridge)) {
        lua.lua_pushstring(L, fengari.to_luastring(funcName));
        lua.lua_pushjsfunction(L, func);
        lua.lua_settable(L, -3);
    }
    lua.lua_setglobal(L, fengari.to_luastring('Graphics'));
    
    // GameData (will be updated each frame)
    lua.lua_newtable(L);
    lua.lua_setglobal(L, fengari.to_luastring('GameData'));
    
    // Config object
    lua.lua_newtable(L);
    for (const [key, value] of Object.entries(dashboard.settings)) {
        lua.lua_pushstring(L, fengari.to_luastring(key));
        pushLuaValue(L, value);
        lua.lua_settable(L, -3);
    }
    lua.lua_setglobal(L, fengari.to_luastring('Config'));
    
    // Width/Height
    lua.lua_pushnumber(L, state.canvas.width);
    lua.lua_setglobal(L, fengari.to_luastring('Width'));
    lua.lua_pushnumber(L, state.canvas.height);
    lua.lua_setglobal(L, fengari.to_luastring('Height'));
}

function pushLuaValue(L, value) {
    const fengari = window.fengari;
    const lua = fengari.lua;
    
    if (typeof value === 'number') {
        lua.lua_pushnumber(L, value);
    } else if (typeof value === 'string') {
        lua.lua_pushstring(L, fengari.to_luastring(value));
    } else if (typeof value === 'boolean') {
        lua.lua_pushboolean(L, value);
    } else if (value === null || value === undefined) {
        lua.lua_pushnil(L);
    } else {
        lua.lua_pushstring(L, fengari.to_luastring(String(value)));
    }
}

// ============================================================================
// Graphics Bridge (Canvas API wrapper for Lua)
// ============================================================================
function createGraphicsBridge() {
    const ctx = state.ctx;
    
    return {
        // Drawing primitives
        DrawLine: (x1, y1, x2, y2, thickness, color) => {
            ctx.save();
            ctx.strokeStyle = colorToCSS(color);
            ctx.lineWidth = thickness || 1;
            ctx.beginPath();
            ctx.moveTo(x1, y1);
            ctx.lineTo(x2, y2);
            ctx.stroke();
            ctx.restore();
        },
        
        DrawRectangle: (x, y, width, height, thickness, color) => {
            ctx.save();
            ctx.strokeStyle = colorToCSS(color);
            ctx.lineWidth = thickness || 1;
            ctx.strokeRect(x, y, width, height);
            ctx.restore();
        },
        
        FillRectangle: (x, y, width, height, color) => {
            ctx.save();
            ctx.fillStyle = colorToCSS(color);
            ctx.fillRect(x, y, width, height);
            ctx.restore();
        },
        
        DrawCircle: (x, y, radius, thickness, color) => {
            ctx.save();
            ctx.strokeStyle = colorToCSS(color);
            ctx.lineWidth = thickness || 1;
            ctx.beginPath();
            ctx.arc(x, y, radius, 0, Math.PI * 2);
            ctx.stroke();
            ctx.restore();
        },
        
        FillCircle: (x, y, radius, color) => {
            ctx.save();
            ctx.fillStyle = colorToCSS(color);
            ctx.beginPath();
            ctx.arc(x, y, radius, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
        },
        
        // Text rendering
        DrawText: (x, y, text, fontSize, color, fontName) => {
            ctx.save();
            ctx.fillStyle = colorToCSS(color);
            ctx.font = `${fontSize}px ${fontName || 'Arial'}`;
            ctx.textBaseline = 'top';
            ctx.fillText(text, x, y);
            ctx.restore();
        },
        
        DrawTextWithBackground: (x, y, text, fontSize, color, bgColor, fontName) => {
            ctx.save();
            ctx.font = `${fontSize}px ${fontName || 'Arial'}`;
            const metrics = ctx.measureText(text);
            const textWidth = metrics.width;
            const textHeight = fontSize * 1.2;
            
            // Draw background
            ctx.fillStyle = colorToCSS(bgColor);
            ctx.fillRect(x, y, textWidth, textHeight);
            
            // Draw text
            ctx.fillStyle = colorToCSS(color);
            ctx.textBaseline = 'top';
            ctx.fillText(text, x, y);
            ctx.restore();
        },
        
        MeasureString: (text, fontSize, fontName) => {
            ctx.save();
            ctx.font = `${fontSize}px ${fontName || 'Arial'}`;
            const metrics = ctx.measureText(text);
            ctx.restore();
            return metrics.width;
        },
        
        // Image rendering
        DrawImage: (imageKey, x, y, width, height) => {
            const img = state.imageCache[imageKey];
            if (img) {
                if (width && height) {
                    ctx.drawImage(img, x, y, width, height);
                } else {
                    ctx.drawImage(img, x, y);
                }
            } else {
                console.warn(`Image not found: ${imageKey}`);
            }
        },
        
        DrawImageRegion: (imageKey, srcX, srcY, srcWidth, srcHeight, destX, destY, destWidth, destHeight) => {
            const img = state.imageCache[imageKey];
            if (img) {
                ctx.drawImage(img, srcX, srcY, srcWidth, srcHeight, destX, destY, destWidth, destHeight);
            }
        },
        
        // Transformations
        ResetTransform: () => {
            ctx.setTransform(1, 0, 0, 1, 0, 0);
        },
        
        Rotate: (angle) => {
            ctx.rotate(angle);
        },
        
        RotateAt: (angle, centerX, centerY) => {
            ctx.translate(centerX, centerY);
            ctx.rotate(angle);
            ctx.translate(-centerX, -centerY);
        },
        
        Translate: (x, y) => {
            ctx.translate(x, y);
        },
        
        Scale: (x, y) => {
            ctx.scale(x, y);
        },
        
        // State management
        Save: () => {
            ctx.save();
        },
        
        Restore: () => {
            ctx.restore();
        },
        
        Clear: (color) => {
            if (color) {
                ctx.fillStyle = colorToCSS(color);
                ctx.fillRect(0, 0, state.canvas.width, state.canvas.height);
            } else {
                ctx.clearRect(0, 0, state.canvas.width, state.canvas.height);
            }
        },
        
        // Advanced
        SetClip: (x, y, width, height) => {
            ctx.save();
            ctx.beginPath();
            ctx.rect(x, y, width, height);
            ctx.clip();
        },
        
        ReleaseClip: () => {
            ctx.restore();
        }
    };
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
        requestAnimationFrame(renderLoop);
    };
    
    state.ws.onmessage = (event) => {
        try {
            const message = JSON.parse(event.data);
            if (message.type === 'gameData') {
                state.gameData = message.data;
                state.gameContext = message.context;
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
            if (state.currentDashboard && !state.ws || state.ws.readyState === WebSocket.CLOSED) {
                connectWebSocket();
            }
        }, 3000);
    };
}

// ============================================================================
// Render Loop
// ============================================================================
function renderLoop(timestamp) {
    if (!state.isRunning) return;
    
    const deltaTime = timestamp - state.lastFrameTime;
    state.lastFrameTime = timestamp;
    
    // Clear canvas
    state.ctx.clearRect(0, 0, state.canvas.width, state.canvas.height);
    
    // Update GameData in Lua
    if (state.gameData) {
        updateLuaGameData(state.gameData);
    }
    
    // Call Lua onUpdate
    const fengari = window.fengari;
    const lua = fengari.lua;
    const L = state.luaEnv;
    
    lua.lua_getglobal(L, fengari.to_luastring('onUpdate'));
    if (lua.lua_isfunction(L, -1)) {
        lua.lua_pushnumber(L, deltaTime / 1000); // Convert to seconds
        if (lua.lua_pcall(L, 1, 0, 0) !== lua.LUA_OK) {
            const error = lua.lua_tojsstring(L, -1);
            console.error('Error in onUpdate:', error);
            lua.lua_pop(L, 1);
        }
    } else {
        lua.lua_pop(L, 1);
    }
    
    requestAnimationFrame(renderLoop);
}

function updateLuaGameData(data) {
    const fengari = window.fengari;
    const lua = fengari.lua;
    const L = state.luaEnv;
    
    lua.lua_getglobal(L, fengari.to_luastring('GameData'));
    if (lua.lua_istable(L, -1)) {
        for (const [key, value] of Object.entries(data)) {
            lua.lua_pushstring(L, fengari.to_luastring(key));
            pushLuaValue(L, value);
            lua.lua_settable(L, -3);
        }
    }
    lua.lua_pop(L, 1);
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
    
    // Set canvas size to match screen
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
    
    state.canvas = canvas;
    state.ctx = canvas.getContext('2d');
    
    container.classList.add('active');
    document.getElementById('menu-button').classList.add('active');
    document.getElementById('connection-status').classList.add('active');
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

// Menu button - back to dashboard selection
document.getElementById('menu-button').addEventListener('click', () => {
    if (state.ws) {
        state.ws.close();
        state.ws = null;
    }
    
    state.isRunning = false;
    state.currentDashboard = null;
    
    // Call Lua onExit
    if (state.luaEnv) {
        const fengari = window.fengari;
        const lua = fengari.lua;
        const L = state.luaEnv;
        
        lua.lua_getglobal(L, fengari.to_luastring('onExit'));
        if (lua.lua_isfunction(L, -1)) {
            lua.lua_pcall(L, 0, 0, 0);
        }
        
        lua.lua_close(L);
        state.luaEnv = null;
    }
    
    document.getElementById('canvas-container').classList.remove('active');
    showDashboardSelector();
});

// Handle window resize
window.addEventListener('resize', () => {
    if (state.canvas) {
        state.canvas.width = window.innerWidth;
        state.canvas.height = window.innerHeight;
        
        // Update Lua globals
        if (state.luaEnv) {
            const fengari = window.fengari;
            const lua = fengari.lua;
            const L = state.luaEnv;
            
            lua.lua_pushnumber(L, state.canvas.width);
            lua.lua_setglobal(L, fengari.to_luastring('Width'));
            lua.lua_pushnumber(L, state.canvas.height);
            lua.lua_setglobal(L, fengari.to_luastring('Height'));
        }
    }
});

// Prevent context menu on long press (mobile)
window.addEventListener('contextmenu', (e) => e.preventDefault());
