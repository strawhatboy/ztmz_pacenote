'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');

const repositoryRoot = path.resolve(__dirname, '..');
const webDashboardRoot = path.join(repositoryRoot, 'src', 'ZTMZ.PacenoteTool.WebDashboard');
const dashboardsRoot = path.join(webDashboardRoot, 'dashboards');
let renderedTextLines = [];
let renderedTextRectangles = [];

function createCanvasContext() {
    return {
        save() {},
        restore() {},
        beginPath() {},
        closePath() {},
        moveTo() {},
        lineTo() {},
        rect() {},
        clip() {},
        fill() {},
        stroke() {},
        fillRect(x, y, width, height) {
            renderedTextRectangles.push({ x, y, width, height });
        },
        strokeRect() {},
        clearRect() {},
        fillText(text, x, y) {
            renderedTextLines.push({ text: String(text), x, y });
        },
        drawImage() {},
        arc(...values) {
            if (!values.every(Number.isFinite)) {
                throw new Error('Canvas arc received a non-finite value.');
            }
        },
        ellipse(...values) {
            if (!values.every(Number.isFinite)) {
                throw new Error('Canvas ellipse received a non-finite value.');
            }
        },
        quadraticCurveTo() {},
        setTransform() {},
        transform() {},
        translate() {},
        rotate() {},
        scale() {},
        measureText(text) {
            return {
                width: String(text ?? '').length * 8,
                fontBoundingBoxAscent: 10,
                fontBoundingBoxDescent: 4
            };
        },
        createLinearGradient() {
            return { addColorStop() {} };
        },
        createRadialGradient() {
            return { addColorStop() {} };
        }
    };
}

function installBrowserGlobals() {
    global.window = global;
    global.addEventListener = () => {};
    global.HTMLDocument = class HTMLDocument {};
    global.Document = global.HTMLDocument;
    global.HTMLElement = class HTMLElement {};
    global.HTMLCollection = class HTMLCollection extends Array {};
    global.NodeList = class NodeList extends Array {};
    global.MutationObserver = class MutationObserver {
        observe() {}
        disconnect() {}
    };
    global.document = Object.assign(new global.HTMLDocument(), {
        documentElement: { dataset: { clientMode: 'web' } },
        createElement(name) {
            if (name !== 'canvas') {
                return { style: {}, dataset: {}, remove() {} };
            }

            return {
                style: {},
                dataset: {},
                width: 1920,
                height: 1080,
                remove() {},
                getContext() {
                    return createCanvasContext();
                }
            };
        },
        getElementsByTagName() {
            return [];
        },
        querySelectorAll() {
            return [];
        },
        addEventListener() {},
        currentScript: null,
        readyState: 'complete'
    });
    global.requestAnimationFrame = callback => setTimeout(callback, 0);
}

function parseJson(source) {
    return JSON.parse(source.replace(/,\s*([}\]])/g, '$1'));
}

function readSettings(folder) {
    const settingsPath = path.join(dashboardsRoot, folder, 'settings.json');
    const settingsFile = parseJson(fs.readFileSync(settingsPath, 'utf8'));
    return Object.fromEntries(
        Object.keys(settingsFile.PropertyName || {}).map((key, index) => [
            key,
            settingsFile.PropertyValue[index]
        ]));
}

function readDescriptor(folder) {
    const descriptorPath = path.join(dashboardsRoot, folder, 'info.json');
    const descriptor = parseJson(fs.readFileSync(descriptorPath, 'utf8'));
    if (descriptor.SupportsWebDashboardClientLua !== true) {
        throw new Error(`${folder}: SupportsWebDashboardClientLua must be true.`);
    }
    return descriptor;
}

function delay(milliseconds) {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
}

async function waitFor(condition, errorMessage, timeoutMilliseconds = 2000) {
    const deadline = Date.now() + timeoutMilliseconds;
    while (!condition()) {
        if (Date.now() >= deadline) {
            throw new Error(errorMessage);
        }
        await delay(10);
    }
}

const telemetry = {
    Brake: 0.7,
    Clutch: 0.3,
    CompletionRate: 0.66,
    Gear: 5,
    G_lat: 0.4,
    G_long: 0.2,
    HandBrake: 0.1,
    HandBrakeValid: true,
    LapDistance: 6600,
    LapTime: 65,
    MaxGears: 6,
    MaxRPM: 9000,
    RPM: 6800,
    ShiftLightsRPMEnd: 8500,
    ShiftLightsRPMStart: 7000,
    ShiftLightsRPMValid: true,
    Speed: 130,
    Steering: 0.15,
    Throttle: 0.5,
    TrackLength: 10000,
    Time: 65,
    GameDataText: 'diagnostic data\r\nsecond line',
    GameSpecificDataText: 'game specific data'
};

const telemetryContext = {
    TrackName: 'Test Stage',
    AudioPackage: 'Test Package',
    ScriptAuthor: 'Test Author',
    PacenoteType: 'Test',
    CarName: 'Toyota [8]',
    CarClass: 'RC1',
    LocalReplayValid: true,
    LocalReplayFinishTime: 98,
    LocalReplay: { checkpoints: 12, trackLength: 10000 },
    GameState: 3,
    LocalReplayDelta: -0.4,
    LocalReplayComparison: {
        Valid: true,
        CurrentCompletionRate: 0.66,
        BestTimeAtCurrentProgress: 65.4,
        DeltaAtCurrentProgress: -0.4,
        BestCompletionRateAtCurrentTime: 0.67,
        BestDistanceAtCurrentTime: 6700
    },
    BestReplayTimeline: Array.from(
        { length: 101 },
        (_, index) => ({ Time: index, Distance: index * 100 }))
};

const browserConfig = {
    HudChromaKeyMode: false,
    HudShiftGreenZone: 0.6,
    HudShiftYellowZone: 0.75,
    HudShiftRedZone: 0.9,
    FrameRate: 60
};

const steeringDashboards = ['classic_dashboard', 'modern_wrc_2025', 'modern_wrc_2025v2', 'liquid_glass'];

// Run inside the actual browser Lua VM. Non-finite Lua values cannot be sent in JSON.
const forceFeedbackProbe = `
for _,value in ipairs({-1,-.99,-.5,0,.5,.99,1}) do
    local state=getForceFeedbackDisplayState({ForceFeedback={OutputFraction=value}})
    assert(state.output==value and state.magnitude==math.abs(value), 'FFB signed state')
end
for _,value in ipairs({-1.001,1.001,0/0,math.huge,-math.huge,'invalid',false}) do
    assert(getForceFeedbackDisplayState({ForceFeedback={OutputFraction=value}})==nil, 'FFB invalid state')
end
assert(getForceFeedbackDisplayState(nil)==nil, 'FFB missing data')
assert(getForceFeedbackDisplayState({})==nil, 'FFB missing snapshot')
assert(getForceFeedbackDisplayState({ForceFeedback={}})==nil, 'FFB missing output')

local originalRail=drawForceFeedbackRail
local visibleRails=0
function drawForceFeedbackRail(gfx,state,font,track,active,limit,background,x,y,width,size)
    local lines,circles,labels={},{},{}
    local spy={
        DrawLine=function(...) table.insert(lines,{...});gfx.DrawLine(...) end,
        FillCircle=function(...) table.insert(circles,{...});gfx.FillCircle(...) end,
        drawTextWithBackgroundCentered=function(...) table.insert(labels,{...});gfx.drawTextWithBackgroundCentered(...) end
    }
    originalRail(spy,state,font,track,active,limit,background,x,y,width,size)
    if state==nil then
        assert(#lines==0 and #circles==0 and #labels==0, 'Unavailable FFB drew a placeholder')
        return
    end
    visibleRails=visibleRails+1
    assert(#lines==(state.output==0 and 2 or 3) and #circles==0 and #labels==0, 'FFB rail primitives')
    for _,line in ipairs(lines) do
        assert(line[6]>=1 and line[1].Color.A>0, 'FFB subpixel or invisible rail')
    end
    assert(lines[1][2]==x and lines[1][4]==x+width, 'FFB track bounds')
    local center=x+width/2
    assert(lines[2][2]==center and lines[2][4]==center, 'FFB center tick')
    local endpoint=center+width/2*state.output
    if state.output~=0 then
        assert(lines[3][2]==center and math.abs(lines[3][4]-endpoint)<.00001, 'FFB signed fill')
    end
    local color=lines[#lines][1].Color
    local expected=(state.magnitude>=.99 and limit or active).Color
    assert(color.R==expected.R and color.G==expected.G and color.B==expected.B, 'FFB output-limit color')
    local margin=math.max(1,size*.5)
    assert(x-size*3.5>=0 and x+width+math.max(1,size*.18)<=gfx.Width and y-margin>=0 and y+margin<=gfx.Height,
        'FFB outside canvas')
end
local originalArc=drawForceFeedbackArc
function drawForceFeedbackArc(gfx,state,font,track,active,limit,background,x,y,radius,size)
    local lines,labels={},{}
    local spy={
        DrawLine=function(...) table.insert(lines,{...});gfx.DrawLine(...) end,
        drawTextWithBackgroundCentered=function(...) table.insert(labels,{...});gfx.drawTextWithBackgroundCentered(...) end
    }
    originalArc(spy,state,font,track,active,limit,background,x,y,radius,size)
    if state==nil then
        assert(#lines==0 and #labels==0, 'Unavailable FFB drew an arc')
        return
    end
    visibleRails=visibleRails+1
    assert(#labels==0, 'FFB arc has unwanted text')
    local fills={}
    for _,line in ipairs(lines) do
        assert(line[6]>=1, 'FFB arc subpixel stroke')
        if line[1]~=track and line[2]~=line[4] then table.insert(fills,line) end
        for _,i in ipairs({2,4}) do assert(line[i]>=0 and line[i]<=gfx.Width, 'FFB arc horizontal bounds') end
        for _,i in ipairs({3,5}) do assert(line[i]>=0 and line[i]<=gfx.Height, 'FFB arc vertical bounds') end
    end
    if state.output==0 then assert(#fills==0, 'Zero FFB filled an arc')
    else
        assert(#fills>0 and math.abs(fills[1][2]-x)<.00001, 'FFB arc starts at center')
        local last=fills[#fills]
        local angle=math.pi*5/6*state.output
        assert(math.abs(last[4]-(x+radius*math.sin(angle)))<.00001 and
            math.abs(last[5]-(y-radius*math.cos(angle)))<.00001, 'FFB signed arc endpoint')
        assert(last[1]==(state.magnitude>=.99 and limit or active), 'FFB arc limit color')
    end
end
local originalCurve=drawForceFeedbackCurve
function drawForceFeedbackCurve(gfx,state,track,active,limit,x,y,halfWidth,height,size)
    local lines={}
    local spy={DrawLine=function(...) table.insert(lines,{...});gfx.DrawLine(...) end}
    originalCurve(spy,state,track,active,limit,x,y,halfWidth,height,size)
    if state==nil then assert(#lines==0,'Unavailable FFB drew a curve');return end
    visibleRails=visibleRails+1
    local fills={}
    for _,line in ipairs(lines) do
        assert(line[6]>=1,'FFB curve subpixel stroke')
        if line[1]~=track then table.insert(fills,line) end
    end
    if state.output==0 then assert(#fills==0,'Zero FFB filled a curve')
    else
        local last=fills[#fills]
        assert(#fills>0 and math.abs(fills[1][2]-x)<.00001,'FFB curve starts at center')
        assert(math.abs(last[4]-(x+halfWidth*state.output))<.00001 and
            math.abs(last[5]-(y+height*state.output*state.output))<.00001,'FFB curve signed endpoint')
        assert(last[1]==(state.magnitude>=.99 and limit or active),'FFB curve limit color')
    end
end
local originalUpdate=onUpdate
function onUpdate(args)
    visibleRails=0
    originalUpdate(args)
    local expected=args.GameData.TestExpectedForceFeedback
    if expected~=nil then assert(visibleRails==expected, 'FFB availability/steering switch') end
end
`;

async function verifyDashboard(folder, id, commonScripts) {
    const scriptPath = path.join(dashboardsRoot, folder, 'script.lua');
    const descriptor = readDescriptor(folder);
    const failures = [];
    let renderedFrames = 0;
    renderedTextLines = [];
    renderedTextRectangles = [];
    const image = { naturalWidth: 320, naturalHeight: 180 };
    const manager = new window.ZtmzBrowserLuaDashboardManager({
        layerStack: () => ({ appendChild() {} }),
        fetchPackage: async () => ({
            supportsWebDashboardClientLua: descriptor.SupportsWebDashboardClientLua,
            luaScript: fs.readFileSync(scriptPath, 'utf8') +
                (steeringDashboards.includes(folder) ? forceFeedbackProbe : ''),
            commonScripts,
            imageResources: {},
            pacenoteIconTokenMap: {
                'left_6': 'left_6'
            },
            pacenoteDisplayNameMap: {
                'left_6': 'Left 6'
            }
        }),
        loadImage: async () => image,
        getImage: () => image,
        requestScene() {},
        onFrameRendered() {
            renderedFrames++;
        },
        onError(message) {
            failures.push(message);
        },
        onFallback(message) {
            failures.push(message);
        }
    });

    manager.handleTelemetry({
        sceneRevision: 1,
        dashboards: [{
            id,
            order: id,
            configurationRevision: 1,
            settings: readSettings(folder)
        }],
        config: browserConfig,
        data: telemetry,
        context: telemetryContext
    });
    await waitFor(
        () => renderedFrames >= 1,
        `${folder}: expected the initial browser-Lua render frame.`);

    const { BestReplayTimeline, ...deltaContext } = telemetryContext;
    manager.handleTelemetry({
        sceneRevision: 1,
        data: { ...telemetry, LapTime: 66, LapDistance: 6700, Steering: 0 },
        context: deltaContext
    });
    await waitFor(
        () => renderedFrames >= 2,
        `${folder}: expected the centered-steering browser-Lua render frame.`);
    if (['modern_wrc', 'modern_wrc_2', 'modern_wrc_2025', 'modern_wrc_2025v2'].includes(folder)) {
        const settings = readSettings(folder);
        let revision = 1;
        const drawHybrid = async (hybrid, changedSettings) => {
            const nextFrame = renderedFrames + 1;
            renderedTextLines = [];
            const data = { ...telemetry };
            // The server omits unavailable snapshots/fields in JSON.
            if (hybrid !== undefined) data.Hybrid = hybrid;
            manager.handleTelemetry({
                sceneRevision: 1, data, context: telemetryContext,
                ...(changedSettings ? { dashboards: [{ id, order: id,
                    configurationRevision: ++revision, settings: changedSettings }] } : {})
            });
            await waitFor(() => renderedFrames >= nextFrame || failures.length > 0,
                `${folder}: hybrid frame did not render.`);
            if (failures.length) throw new Error(`${folder}: ${failures.join(' | ')}`);
            return renderedTextLines.map(line => line.text.trim());
        };
        for (const state of ['BOOST', 'READY', 'REGEN', 'STANDBY']) {
            const lines = await drawHybrid({ BatteryChargeFraction: .8, BoostProgressFraction: .55,
                BoostMap: 2, BoostActive: state === 'BOOST', BoostReady: state === 'READY',
                RegenerationActive: state === 'REGEN' });
            if (!lines.includes('80%') || (folder === 'modern_wrc_2' ? lines.includes(state) : !lines.includes(state)) || !lines.includes('M2'))
                throw new Error(`${folder}: missing hybrid battery/state/map: ${lines.join(' | ')}`);
        }
        const partial = await drawHybrid({ BoostProgressFraction: .5, BoostActive: true });
        if (!partial.includes('--') || partial.includes('0%') || partial.includes('M2'))
            throw new Error(`${folder}: missing hybrid fields were invented or retained.`);
        const empty = await drawHybrid(undefined);
        if (empty.includes('--') || empty.includes('BOOST') || empty.includes('80%'))
            throw new Error(`${folder}: hybrid readings remained after a car change.`);
        const disabled = await drawHybrid({ BatteryChargeFraction: .8 },
            { ...settings, 'dashboards.settings.showHybrid': false });
        if (disabled.includes('80%')) throw new Error(`${folder}: hybrid visibility setting ignored.`);
        for (const size of [.1, 1]) {
            const zero = await drawHybrid({ BatteryChargeFraction: 0, BoostProgressFraction: 0 },
                { ...settings, 'dashboards.settings.size': size, 'dashboards.settings.showHybrid': true,
                    'dashboards.settings.switchGearNSpeed': !settings['dashboards.settings.switchGearNSpeed'] });
            if (!zero.includes('0%')) throw new Error(`${folder}: zero charge was treated as unavailable.`);
        }
    }
    if (steeringDashboards.includes(folder)) {
        let revision = 100;
        const settings = readSettings(folder);
        const switchKey = folder === 'classic_dashboard' ? 'showSteering' : 'showSteeringIndicator';
        const drawFeedback = async (snapshot, visible, changedSettings) => {
            const nextFrame = renderedFrames + 1;
            renderedTextLines = [];
            renderedTextRectangles = [];
            const data = { ...telemetry, TestExpectedForceFeedback: visible ? 1 : 0 };
            if (snapshot !== undefined) data.ForceFeedback = snapshot;
            manager.handleTelemetry({
                sceneRevision: 1, data, context: telemetryContext,
                ...(changedSettings ? { dashboards: [{ id, order: id, configurationRevision: ++revision,
                    settings: { ...settings, ...changedSettings } }] } : {})
            });
            await waitFor(() => renderedFrames >= nextFrame || failures.length > 0,
                `${folder}: FFB frame did not render.`);
            if (failures.length) throw new Error(`${folder}: ${failures.join(' | ')}`);
            const shown = renderedTextLines.some(line => line.text.trim() === 'FFB');
            if (shown) throw new Error(`${folder}: FFB label should be absent.`);
            return renderedTextRectangles.reduce((largest, rectangle) =>
                !largest || rectangle.width * rectangle.height > largest.width * largest.height
                    ? rectangle : largest, null);
        };
        const emptyBounds = await drawFeedback(undefined, false, {});
        for (const output of [-1, -.99, -.989, -.5, 0, .5, .989, .99, 1]) {
            const bounds = await drawFeedback({ OutputFraction: output }, true);
            if (folder === 'classic_dashboard' && (!emptyBounds || JSON.stringify(bounds) !== JSON.stringify(emptyBounds))) {
                throw new Error('classic_dashboard: FFB added space outside the existing instrument cells.');
            }
        }
        for (const snapshot of [undefined, null, {}, { OutputFraction: null },
            { OutputFraction: -1.001 }, { OutputFraction: 1.001 }, { OutputFraction: 'invalid' }]) {
            await drawFeedback(snapshot, false);
        }
        if (folder !== 'liquid_glass') {
            await drawFeedback({ OutputFraction: .5 }, false, { [`dashboards.settings.${switchKey}`]: false });
        }
        // Classic's six square cells must fit the existing 1280x720 canvas.
        for (const size of [.1, folder === 'classic_dashboard' ? .24 : .32]) {
            for (const position of [-1, 0, 1]) {
                await drawFeedback({ OutputFraction: -.5 }, true, {
                    'dashboards.settings.size': size,
                    'dashboards.settings.positionH': position,
                    'dashboards.settings.positionV': position,
                    'dashboards.settings.switchGearNSpeed': true
                });
            }
        }
        // Remove on the same runtime after rendering, then restore a valid zero.
        await drawFeedback(undefined, false);
        await drawFeedback({ OutputFraction: 0 }, true);
    }
    manager.suspend();

    if (failures.length > 0) {
        throw new Error(`${folder}: ${failures.join(' | ')}`);
    }
    if (renderedFrames < 2) {
        throw new Error(`${folder}: expected two browser-Lua render frames, received ${renderedFrames}.`);
    }
    if (folder === 'debug') {
        const firstLine = renderedTextLines.find(line => line.text === 'diagnostic data');
        const secondLine = renderedTextLines.find(line => line.text === 'second line');
        const hasMultilineTextCall = renderedTextLines.some(line =>
            line.text.includes('\r') || line.text.includes('\n'));
        const hasNativePadding = renderedTextRectangles.some(rectangle =>
            rectangle.x === -3.5
            && rectangle.y === -3.5
            && rectangle.width === 127
            && rectangle.height === 35);
        if (!firstLine
            || !secondLine
            || hasMultilineTextCall
            || firstLine.x !== 0
            || firstLine.y !== 10
            || secondLine.x !== 0
            || secondLine.y !== 24
            || !hasNativePadding) {
            throw new Error('debug: expected CRLF telemetry text to use native layout coordinates.');
        }
    }
    if (folder === 'mapInfo') {
        const renderedMapInfoText = renderedTextLines.map(line => line.text).join(' | ');
        if (!renderedMapInfoText.includes('Racing')) {
            throw new Error(`mapInfo: expected game state 3 to render as Racing; rendered: ${renderedMapInfoText}`);
        }
    }
}

async function verifyModernProgressWithoutBestPosition(commonScripts) {
    const folder = 'modern_progress';
    const descriptor = readDescriptor(folder);
    const failures = [];
    let renderedFrames = 0;
    const image = { naturalWidth: 320, naturalHeight: 180 };
    const manager = new window.ZtmzBrowserLuaDashboardManager({
        layerStack: () => ({ appendChild() {} }),
        fetchPackage: async () => ({
            supportsWebDashboardClientLua: descriptor.SupportsWebDashboardClientLua,
            luaScript: fs.readFileSync(path.join(dashboardsRoot, folder, 'script.lua'), 'utf8'),
            commonScripts,
            imageResources: {}
        }),
        loadImage: async () => image,
        getImage: () => image,
        requestScene() {},
        onFrameRendered() {
            renderedFrames++;
        },
        onError(message) {
            failures.push(message);
        },
        onFallback(message) {
            failures.push(message);
        }
    });

    manager.handleTelemetry({
        sceneRevision: 1,
        dashboards: [{
            id: 600,
            order: 600,
            configurationRevision: 1,
            settings: readSettings(folder)
        }],
        config: browserConfig,
        data: telemetry,
        context: {
            ...telemetryContext,
            BestReplayTimeline: []
        }
    });
    await waitFor(
        () => renderedFrames >= 1,
        'modern_progress: expected a render when the best replay has no position.');
    manager.suspend();

    if (failures.length > 0) {
        throw new Error(`modern_progress: empty best replay timeline caused a fallback: ${failures.join(' | ')}`);
    }
}

async function verifySustainedTelemetry() {
    const failures = [];
    const manager = new window.ZtmzBrowserLuaDashboardManager({
        layerStack: () => ({ appendChild() {} }),
        requestScene() {},
        onFrameRendered() {},
        onError(message) {
            failures.push(message);
        },
        onFallback(message) {
            failures.push(message);
        }
    });
    const context = {};
    manager.handleTelemetry({
        sceneRevision: 1,
        dashboards: [],
        config: browserConfig,
        data: { ...telemetry, LapTime: 0, LapDistance: 0 },
        context
    });

    for (let index = 1; index <= 10000; index++) {
        manager.handleTelemetry({
            sceneRevision: 1,
            data: {
                ...telemetry,
                LapTime: index * 0.1,
                LapDistance: index * 2
            },
            context
        });
    }
    await delay(20);

    const lookup = manager.currentReplayLookup;
    const interpolatedTime = lookup.getTimeByDistance(10000);
    if (failures.length > 0
        || lookup.samples.length > 4096
        || Math.abs(interpolatedTime - 500) > 0.2) {
        throw new Error('Browser Lua sustained telemetry lookup is not bounded and accurate.');
    }

    manager.handleTelemetry({
        sceneRevision: 1,
        data: { ...telemetry, LapTime: 0.1, LapDistance: 1 },
        context
    });
    if (lookup.samples.length !== 1 || lookup.samples[0].time !== 0.1) {
        throw new Error('Browser Lua sustained telemetry lookup did not reset for a new stage.');
    }
    manager.suspend();
}

function verifyInboundTelemetryCoalescing() {
    const dashboardClientPath = path.join(
        repositoryRoot,
        'src',
        'ZTMZ.PacenoteTool.WebDashboard',
        'wwwroot',
        'dashboard-client.js');
    const dashboardClientSource = fs.readFileSync(dashboardClientPath, 'utf8');
    const scheduledFrames = [];
    const originalRequestAnimationFrame = global.requestAnimationFrame;
    global.requestAnimationFrame = callback => {
        scheduledFrames.push(callback);
        return scheduledFrames.length;
    };

    try {
        vm.runInThisContext(
            `${dashboardClientSource}\n`
            + 'globalThis.__ztmzDashboardClientTestHooks = { state, queueBrowserLuaTelemetry };',
            { filename: dashboardClientPath });
        const { state, queueBrowserLuaTelemetry } = global.__ztmzDashboardClientTestHooks;
        const receivedMessages = [];
        state.browserLuaManager = {
            handleTelemetry(message) {
                receivedMessages.push(message);
            },
            suspend() {},
            requestImageRefresh() {}
        };

        queueBrowserLuaTelemetry(JSON.stringify({
            type: 'browserLuaTelemetry',
            sceneRevision: 1,
            data: { LapTime: -1 },
            context: {}
        }));
        queueBrowserLuaTelemetry(JSON.stringify({
            type: 'browserLuaTelemetry',
            sceneRevision: 1,
            dashboards: [],
            config: {},
            data: { LapTime: 0 },
            context: {}
        }));
        for (let index = 1; index <= 10000; index++) {
            queueBrowserLuaTelemetry(JSON.stringify({
                type: 'browserLuaTelemetry',
                sceneRevision: 1,
                data: { LapTime: index },
                context: {}
            }));
        }

        if (scheduledFrames.length !== 1) {
            throw new Error('Browser Lua telemetry burst scheduled more than one browser frame.');
        }
        scheduledFrames[0]();

        if (receivedMessages.length !== 2
            || receivedMessages[0].data.LapTime !== 0
            || receivedMessages[1].data.LapTime !== 10000) {
            throw new Error('Browser Lua telemetry coalescing did not retain the scene and newest frame.');
        }
    } finally {
        global.requestAnimationFrame = originalRequestAnimationFrame;
        delete global.__ztmzDashboardClientTestHooks;
    }
}

async function verifyLanguageSwitch(commonScripts) {
    const failures = [];
    const manager = new window.ZtmzBrowserLuaDashboardManager({
        layerStack: () => ({ appendChild() {} }),
        fetchPackage: async () => ({
            supportsWebDashboardClientLua: true,
            luaScript: fs.readFileSync(path.join(dashboardsRoot, 'liquid_glass', 'script.lua'), 'utf8'),
            commonScripts, imageResources: {},
            translations: { 'dashboards.liquidGlass.labels.coolant': 'STALE PACKAGE' }
        }),
        requestScene() {}, onFrameRendered() {},
        onError: message => failures.push(message),
        onFallback: message => failures.push(message)
    });
    try {
        for (const [index, culture] of ['zh-cn', 'en-us'].entries()) {
            const labels = parseJson(fs.readFileSync(path.join(dashboardsRoot, 'lang', `${culture}.liquid-glass.json`), 'utf8'))
                .dashboards.liquidGlass.labels;
            const translations = Object.fromEntries(Object.entries(labels).map(([key, value]) => [`dashboards.liquidGlass.labels.${key}`, value]));
            renderedTextLines = [];
            manager.handleTelemetry({
                sceneRevision: index + 1,
                dashboards: [{ id: 0, order: 0, configurationRevision: 1, settings: readSettings('liquid_glass') }],
                config: { ...browserConfig, CultureName: culture, Translations: translations },
                data: { ...telemetry, Engine: { CoolantTemperatureCelsius: 94 } },
                context: telemetryContext
            });
            await waitFor(() => failures.length > 0 || renderedTextLines.some(line => line.text === labels.coolant),
                `Liquid Glass did not switch to ${culture}`);
            if (failures.length || renderedTextLines.some(line => line.text === 'STALE PACKAGE')) {
                throw new Error(`Dashboard localization failed: ${failures.join('; ')}`);
            }
            const wrongLabel = culture === 'zh-cn' ? 'COOLANT' : '水温';
            if (renderedTextLines.some(line => line.text === wrongLabel)) throw new Error('Stale language survived scene switch');
        }
    } finally { manager.suspend(); }
}

async function main() {
    installBrowserGlobals();
    global.fengari = require(path.join(
        repositoryRoot,
        'src',
        'ZTMZ.PacenoteTool.WebDashboard',
        'wwwroot',
        'vendor',
        'fengari-web.js'));
    require(path.join(
        repositoryRoot,
        'src',
        'ZTMZ.PacenoteTool.WebDashboard',
        'wwwroot',
        'browser-lua-dashboard.js'));

    const commonScripts = fs.readdirSync(path.join(dashboardsRoot, 'common'))
        .filter(name => name.endsWith('.lua'))
        .map(name => fs.readFileSync(path.join(dashboardsRoot, 'common', name), 'utf8'));
    const folders = fs.readdirSync(dashboardsRoot, { withFileTypes: true })
        .filter(entry => entry.isDirectory()
            && entry.name !== 'common'
            && fs.existsSync(path.join(dashboardsRoot, entry.name, 'script.lua')))
        .map(entry => entry.name)
        .sort();

    for (const [index, folder] of folders.entries()) {
        await verifyDashboard(folder, index, commonScripts);
    }
    await verifyLanguageSwitch(commonScripts);
    await verifyModernProgressWithoutBestPosition(commonScripts);
    await verifySustainedTelemetry();
    verifyInboundTelemetryCoalescing();

    console.log(`Browser Lua smoke passed for ${folders.length} bundled dashboards.`);
}

main().catch(error => {
    console.error(error.stack || error);
    process.exitCode = 1;
});
