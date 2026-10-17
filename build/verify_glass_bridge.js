'use strict';

// Focused bridge contract checks; no browser/GPU dependency. Exercises real Lua calls,
// native host command emission, and both canvas paths with a strict recording context.
const assert = require('assert/strict');
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const root = path.resolve(__dirname, '..');
const web = path.join(root, 'src/ZTMZ.PacenoteTool.WebDashboard/wwwroot');
global.window = global;
global.addEventListener = () => {};
global.HTMLDocument = class {};
global.Document = global.HTMLDocument;
global.HTMLElement = class {};
global.HTMLCollection = class extends Array {};
global.NodeList = class extends Array {};
global.MutationObserver = class { observe() {} disconnect() {} };
let fills = 0, rims = 0, depth = 0;
function finite(...values) { assert(values.every(Number.isFinite), 'Non-finite canvas coordinate'); }
function context() {
    return {
        save() { depth++; }, restore() { depth--; assert(depth >= 0); },
        beginPath() {}, closePath() {}, moveTo: finite, lineTo: finite,
        quadraticCurveTo: finite, rect: finite, roundRect: finite,
        fill() { fills++; }, stroke() { rims++; },
        setTransform() {}, clearRect() {}, drawImage() {},
        measureText() { return {width: 0}; }
    };
}
global.document = Object.assign(new HTMLDocument(), {
    documentElement: {dataset: {clientMode: 'web'}},
    createElement: () => ({style: {}, dataset: {}, remove() {}, getContext: context}),
    getElementsByTagName: () => [], querySelectorAll: () => [], addEventListener() {},
    currentScript: null, readyState: 'complete'
});
global.requestAnimationFrame = callback => setTimeout(callback, 0);
global.fengari = require(path.join(web, 'vendor/fengari-web.js'));
require(path.join(web, 'browser-lua-dashboard.js'));

function runLua(source) {
    const {lua, lauxlib, lualib, to_luastring, to_jsstring} = fengari;
    const state = lauxlib.luaL_newstate(); lualib.luaL_openlibs(state);
    try {
        const status = lauxlib.luaL_dostring(state, to_luastring(source));
        if (status !== lua.LUA_OK) throw new Error(to_jsstring(lua.lua_tostring(state, -1)));
    } finally { lua.lua_close(state); }
}

function verifyNativeHost() {
    const host = fs.readFileSync(path.join(root, 'src/ZTMZ.PacenoteTool.NativeDashboard/lua/host.lua'), 'utf8');
    runLua(`
        local bridge = assert(load([====[${host}]====]))({}, {})
        function onUpdate(args)
            assert(args.SupportsGlassPanel and not args.SupportsGlassRefraction)
            local g = args.Graphics
            local b = g.CreateSolidBrush(18,52,86,140)
            g.DrawGlassPanel(b,10,20,210,120,16,12,6)
            g.DrawGlassPanel(b,-1e20,-1e20,1e20,1e20,1e20,1e20,1e20)
            g.DrawGlassPanel(b,0,0,100,20,0/0,math.huge,0/0)
            g.DrawGlassPanel(b,0,0,100,20,99,-1,-1)
            g.DrawGlassPanel(b,0/0,0,10,10,1)
            g.DrawGlassPanel(b,0,math.huge,10,10,1)
            g.DrawGlassPanel(b,0,0,-10,10,1)
            g.DrawGlassPanel(b,0,0,10,0,1)
        end
        bridge.init()
        local _,c=bridge.update({}, {}, 0, '')
        assert(#c==4)
        assert(c[1].type=='drawGlassPanel' and c[1].x==10 and c[1].y==20)
        assert(c[1].width==200 and c[1].height==100 and c[1].radius==16)
        assert(c[1].refraction==12 and c[1].blur==6 and c[1].color==0x8c123456)
        local keys=0;for _ in pairs(c[1]) do keys=keys+1 end;assert(keys==9)
        assert(c[2].x==-32768 and c[2].width==32768 and c[2].radius==16384)
        assert(c[2].refraction==64 and c[2].blur==32)
        assert(c[3].radius==0 and c[3].refraction==8 and c[3].blur==4)
        assert(c[4].radius==10 and c[4].refraction==0 and c[4].blur==0)
    `);
}

async function verifyBrowserLua() {
    fills = rims = depth = 0;
    let frames = 0;
    const failures = [];
    const manager = new ZtmzBrowserLuaDashboardManager({
        layerStack: () => ({appendChild() {}}), requestScene() {},
        onFrameRendered() { frames++; },
        onError: message => failures.push(message), onFallback: message => failures.push(message),
        fetchPackage: async () => ({supportsWebDashboardClientLua: true, commonScripts: [], imageResources: {},
            luaScript: `function onUpdate(args)
                assert(args.SupportsGlassPanel and not args.SupportsGlassRefraction)
                local g=args.Graphics
                assert(g.SupportsGlassPanel and not g.SupportsGlassRefraction)
                local b=g.CreateSolidBrush(18,52,86,140)
                g.DrawGlassPanel(b,10,20,210,120,16,12,6)
                g.DrawGlassPanel(b,-1e20,-1e20,1e20,1e20,1e20,1e20,1e20)
                g.DrawGlassPanel(b,0,0,100,20,0/0,math.huge,0/0)
                g.DrawGlassPanel(b,0/0,0,100,20,10)
                g.DrawGlassPanel(b,0,math.huge,100,20,10)
                g.DrawGlassPanel(b,0,0,-10,20,10)
                g.DrawGlassPanel(b,0,0,100,0,10)
            end`})
    });
    try {
        manager.handleTelemetry({sceneRevision: 1,
            dashboards: [{id: 0, order: 0, configurationRevision: 1, settings: {}}],
            config: {FrameRate: 60}, data: {}, context: {}});
        const deadline = Date.now() + 2000;
        while (!frames && !failures.length && Date.now() < deadline)
            await new Promise(resolve => setTimeout(resolve, 10));
        assert.deepEqual(failures, []); assert.equal(frames, 1);
        assert.equal(fills, 3); assert.equal(rims, 3);
        // Runtime retains one base save on each of its static and dynamic canvases.
        assert.equal(depth, 2);
    } finally { manager.suspend(); }
}

function verifyCommandPlayer() {
    const source = fs.readFileSync(path.join(web, 'dashboard-client.js'), 'utf8');
    vm.runInThisContext(source + '\nglobalThis.glassCommandTest = executeDrawingCommand;');
    fills = rims = depth = 0;
    const ctx = context();
    for (const geometry of [
        {x: 10, y: 20, width: 200, height: 100},
        {x: -1e20, y: 1e20, width: 1e20, height: 1e20},
        {x: NaN, y: 0, width: 10, height: 10},
        {x: 0, y: 0, width: Infinity, height: 10},
        {x: 0, y: 0, width: -1, height: 10}
    ]) glassCommandTest(ctx, {}, {type: 'drawGlassPanel', ...geometry, radius: Infinity, color: 0x8c123456});
    assert.equal(fills, 2); assert.equal(rims, 2); assert.equal(depth, 0);
    delete global.glassCommandTest;
}

(async () => {
    verifyNativeHost();
    await verifyBrowserLua();
    verifyCommandPlayer();
    console.log('Glass bridge checks passed: native Lua wire contract, browser Lua fallback, command player fallback.');
})().catch(error => { console.error(error); process.exitCode = 1; });
