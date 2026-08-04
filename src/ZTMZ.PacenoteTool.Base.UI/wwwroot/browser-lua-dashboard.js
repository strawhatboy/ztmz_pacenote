/*
 * Opt-in browser Lua dashboard runtime.
 *
 * Only dashboards explicitly selected by the desktop server reach this code. The
 * runtime keeps one isolated Lua state per dashboard and exposes a narrow Canvas
 * facade matching the APIs used by the maintained WRC dashboards.
 */
(function () {
    'use strict';

    const RENDER_WIDTH = 1920;
    const RENDER_HEIGHT = 1080;
    const DASHBOARD_LAYER_Z_INDEX_STRIDE = 1000;

    class BrowserLuaDashboardManager {
        constructor(options) {
            this.options = options;
            this.runtimes = new Map();
            this.sceneRevision = 0;
            this.sceneToken = 0;
            this.sceneReady = false;
            this.pendingFrame = null;
            this.context = {};
            this.renderScheduled = false;
            this.disabled = false;
            this.fallbackNotified = false;
            this.currentReplayLookup = new BrowserLuaReplayLookup({
                maximumSampleCount: 4096,
                minimumTimeDelta: 0.05,
                minimumDistanceDelta: 0.5
            });
        }

        static isSupported() {
            return Boolean(
                window.fengari
                && window.fengari.lua
                && window.fengari.lauxlib
                && window.fengari.lualib
                && window.fengari.interop);
        }

        get isSupported() {
            return BrowserLuaDashboardManager.isSupported();
        }

        get canAdvertiseSupport() {
            return this.isSupported && !this.disabled;
        }

        handleTelemetry(message) {
            const sceneRevision = Number(message.sceneRevision);
            if (!Number.isFinite(sceneRevision) || sceneRevision <= 0 || this.disabled) {
                return;
            }

            if (Array.isArray(message.dashboards)) {
                if (sceneRevision < this.sceneRevision) {
                    return;
                }

                this.sceneRevision = sceneRevision;
                this.context = {};
                this.loadScene(sceneRevision, message.dashboards, message.config || {});
            } else if (sceneRevision !== this.sceneRevision) {
                this.options.requestScene();
                return;
            }

            this.context = {
                ...this.context,
                ...(message.context || {})
            };
            const telemetry = message.data || {};
            this.currentReplayLookup.record(telemetry.LapTime, telemetry.LapDistance);
            this.pendingFrame = {
                data: telemetry,
                context: this.context
            };
            this.scheduleRender();
        }

        suspend() {
            if (this.disabled) {
                return;
            }

            this.resetScene();
        }

        disable() {
            this.disabled = true;
            this.resetScene();
        }

        resetScene() {
            this.sceneReady = false;
            this.sceneRevision = 0;
            this.sceneToken++;
            this.pendingFrame = null;
            this.context = {};
            for (const runtime of this.runtimes.values()) {
                runtime.dispose();
            }
            this.runtimes.clear();
        }

        requestImageRefresh(imageKey) {
            for (const runtime of this.runtimes.values()) {
                if (runtime.usesImage(imageKey)) {
                    runtime.markStaticDirty();
                }
            }
            this.scheduleRender();
        }

        async loadScene(sceneRevision, descriptors, globalConfig) {
            const token = ++this.sceneToken;
            this.sceneReady = false;

            try {
                const nextRuntimes = new Map();
                const descriptorsById = new Map();
                for (const descriptor of descriptors) {
                    const id = Number(readDashboardDescriptorValue(descriptor, 'id'));
                    if (!Number.isInteger(id) || id < 0 || descriptorsById.has(id)) {
                        continue;
                    }
                    descriptorsById.set(id, descriptor);
                }

                const configSignature = JSON.stringify(globalConfig || {});
                for (const descriptor of Array.from(descriptorsById.values()).sort((left, right) =>
                    Number(readDashboardDescriptorValue(left, 'order')) - Number(readDashboardDescriptorValue(right, 'order')))) {
                    if (token !== this.sceneToken || this.disabled) {
                        return;
                    }

                    const id = Number(readDashboardDescriptorValue(descriptor, 'id'));
                    const configurationRevision = Number(readDashboardDescriptorValue(descriptor, 'configurationRevision')) || 0;
                    const settingsSignature = JSON.stringify(readDashboardDescriptorValue(descriptor, 'settings') || {});
                    const current = this.runtimes.get(id);
                    if (current
                        && current.configurationRevision === configurationRevision
                        && current.settingsSignature === settingsSignature
                        && current.configSignature === configSignature) {
                        current.setOrder(Number(readDashboardDescriptorValue(descriptor, 'order')) || 0);
                        nextRuntimes.set(id, current);
                        continue;
                    }

                    const dashboardPackage = await this.options.fetchPackage(id);
                    if (token !== this.sceneToken || this.disabled) {
                        return;
                    }

                    if (!dashboardPackage || dashboardPackage.supportsWebDashboardClientLua !== true) {
                        throw new Error(`Dashboard ${id} does not provide a browser Lua package.`);
                    }

                    current?.dispose();
                    const runtime = new BrowserLuaDashboardRuntime(
                        this,
                        descriptor,
                        globalConfig || {},
                        dashboardPackage);
                    try {
                        await runtime.initialize();
                    } catch (error) {
                        runtime.dispose();
                        throw error;
                    }
                    if (token !== this.sceneToken || this.disabled) {
                        runtime.dispose();
                        return;
                    }
                    nextRuntimes.set(id, runtime);
                }

                if (token !== this.sceneToken || this.disabled) {
                    for (const [id, runtime] of nextRuntimes) {
                        if (this.runtimes.get(id) !== runtime) {
                            runtime.dispose();
                        }
                    }
                    return;
                }

                for (const [id, runtime] of this.runtimes) {
                    if (!nextRuntimes.has(id)) {
                        runtime.dispose();
                    }
                }

                this.runtimes = nextRuntimes;
                this.sceneRevision = sceneRevision;
                this.sceneReady = true;
                this.scheduleRender();
            } catch (error) {
                this.notifyFallback(error);
            }
        }

        scheduleRender() {
            if (this.renderScheduled || !this.pendingFrame || !this.sceneReady || this.disabled) {
                return;
            }

            this.renderScheduled = true;
            requestAnimationFrame(() => {
                this.renderScheduled = false;
                this.renderLatestFrame();
            });
        }

        renderLatestFrame() {
            if (!this.pendingFrame || !this.sceneReady || this.disabled) {
                return;
            }

            const frame = this.pendingFrame;
            try {
                for (const runtime of Array.from(this.runtimes.values()).sort((left, right) => left.order - right.order)) {
                    try {
                        runtime.render(frame);
                    } catch (error) {
                        const message = error instanceof Error ? error.message : String(error);
                        const dashboardError = new Error(`Dashboard ${runtime.id}: ${message}`);
                        if (error instanceof Error && error.stack) {
                            dashboardError.stack = `${dashboardError.stack}\nCaused by: ${error.stack}`;
                        }
                        throw dashboardError;
                    }
                }
                this.options.onFrameRendered();
            } catch (error) {
                this.notifyFallback(error);
            }
        }

        notifyFallback(error) {
            if (this.fallbackNotified) {
                return;
            }

            this.fallbackNotified = true;
            const message = error instanceof Error ? error.message : String(error);
            console.error('Browser Lua dashboard failed:', error);
            this.options.onError(message);
            this.options.onFallback(message);
            this.disable();
        }
    }

    class BrowserLuaDashboardRuntime {
        constructor(manager, descriptor, globalConfig, dashboardPackage) {
            this.manager = manager;
            this.id = Number(readDashboardDescriptorValue(descriptor, 'id'));
            this.order = Number(readDashboardDescriptorValue(descriptor, 'order')) || 0;
            this.configurationRevision = Number(readDashboardDescriptorValue(descriptor, 'configurationRevision')) || 0;
            this.settings = readDashboardDescriptorValue(descriptor, 'settings') || dashboardPackage.settings || {};
            this.settingsSignature = JSON.stringify(this.settings);
            this.globalConfig = globalConfig;
            this.configSignature = JSON.stringify(globalConfig);
            this.dashboardPackage = dashboardPackage;
            this.imageResources = {};
            Object.defineProperty(this.imageResources, 'ContainsKey', {
                enumerable: false,
                value: luaFunction(key => Object.prototype.hasOwnProperty.call(
                    this.imageResources,
                    toJavaScriptString(key)))
            });
            this.imageCacheKeys = new Set();
            this.staticDirty = true;
            this.contextSignature = '';
            this.luaState = null;
            this.bestReplayLookup = new BrowserLuaReplayLookup();
            this.bestReplayTimelineSource = null;

            this.staticCanvas = this.createCanvas('static');
            this.dynamicCanvas = this.createCanvas('dynamic');
            this.staticContext = this.staticCanvas.getContext('2d');
            this.dynamicContext = this.dynamicCanvas.getContext('2d');
            this.activeContext = this.dynamicContext;
            this.graphics = new BrowserLuaGraphics(this);
            this.host = this.createHost();
            this.setOrder(this.order);
        }

        async initialize() {
            const imagePromises = [];
            const resourcePrefix = `${this.id}:`;
            for (const [cacheKey, url] of Object.entries(this.dashboardPackage.imageResources || {})) {
                const resourceKey = cacheKey.startsWith(resourcePrefix)
                    ? cacheKey.substring(resourcePrefix.length)
                    : cacheKey;
                this.imageResources[resourceKey] = new BrowserLuaImage(this.manager, cacheKey, url);
                this.imageCacheKeys.add(cacheKey);
                imagePromises.push(this.manager.options.loadImage(cacheKey, url));
            }
            await Promise.all(imagePromises);

            this.luaState = new BrowserLuaState(this.host);
            for (let index = 0; index < (this.dashboardPackage.commonScripts || []).length; index++) {
                this.luaState.runChunk(
                    this.dashboardPackage.commonScripts[index],
                    `dashboard-${this.id}-common-${index}.lua`);
            }
            this.luaState.runChunk(this.dashboardPackage.luaScript || '', `dashboard-${this.id}.lua`);
            this.luaState.runChunk(
                [
                    'function __ztmz_dashboard_init()',
                    '  if onInit then return onInit(__ztmz_dashboard_host.args) end',
                    'end',
                    'function __ztmz_dashboard_update()',
                    '  if onUpdate then return onUpdate(__ztmz_dashboard_host.args) end',
                    'end',
                    'function __ztmz_dashboard_exit()',
                    '  if onExit then return onExit() end',
                    'end'
                ].join('\n'),
                `dashboard-${this.id}-bridge.lua`);
            this.luaState.callGlobal('__ztmz_dashboard_init');
        }

        createHost() {
            const runtime = this;
            const context = {
                LocalReplayValid: false,
                LocalReplay: { checkpoints: 0, trackLength: 0 },
                LocalReplayFinishTime: 0,
                GameState: 0,
                LocalReplayDelta: 0,
                LocalReplayComparison: {
                    Valid: false,
                    CurrentCompletionRate: 0,
                    BestTimeAtCurrentProgress: 0,
                    DeltaAtCurrentProgress: 0,
                    BestCompletionRateAtCurrentTime: 0,
                    BestDistanceAtCurrentTime: 0
                },
                GetDelta: {
                    Invoke: luaMethod(distance => {
                        const currentData = runtime.host?.args?.GameData || {};
                        const requestedDistance = finiteNumber(distance, 0);
                        const comparison = context.LocalReplayComparison || {};
                        if (Math.abs(requestedDistance - finiteNumber(currentData.LapDistance, 0)) < 0.01
                            && comparison.Valid) {
                            return finiteNumber(comparison.DeltaAtCurrentProgress, 0);
                        }

                        const referenceDistance = mapCurrentDistanceToReferenceDistance(
                            requestedDistance,
                            currentData.TrackLength,
                            getReferenceTrackLength(context, runtime.bestReplayLookup));
                        return runtime.manager.currentReplayLookup.getTimeByDistance(requestedDistance)
                            - runtime.bestReplayLookup.getTimeByDistance(referenceDistance);
                    })
                },
                GetDeltaByDistanceAndTime: {
                    Invoke: luaMethod((distance, time) => {
                        const currentData = runtime.host?.args?.GameData || {};
                        const requestedDistance = finiteNumber(distance, 0);
                        const requestedTime = finiteNumber(time, 0);
                        const comparison = context.LocalReplayComparison || {};
                        if (Math.abs(requestedDistance - finiteNumber(currentData.LapDistance, 0)) < 0.01
                            && Math.abs(requestedTime - finiteNumber(currentData.LapTime, 0)) < 0.01) {
                            return comparison.Valid
                                ? finiteNumber(comparison.DeltaAtCurrentProgress, 0)
                                : finiteNumber(context.LocalReplayDelta, 0);
                        }

                        const referenceDistance = mapCurrentDistanceToReferenceDistance(
                            requestedDistance,
                            currentData.TrackLength,
                            getReferenceTrackLength(context, runtime.bestReplayLookup));
                        return requestedTime - runtime.bestReplayLookup.getTimeByDistance(referenceDistance);
                    })
                },
                GetBestTimeByDistance: {
                    Invoke: luaMethod(distance => {
                        const currentData = runtime.host?.args?.GameData || {};
                        return runtime.bestReplayLookup.getTimeByDistance(
                            mapCurrentDistanceToReferenceDistance(
                                finiteNumber(distance, 0),
                                currentData.TrackLength,
                                getReferenceTrackLength(context, runtime.bestReplayLookup)));
                    })
                },
                GetBestDistanceByTime: {
                    Invoke: luaMethod(time => {
                        const currentData = runtime.host?.args?.GameData || {};
                        return mapReferenceDistanceToCurrentDistance(
                            runtime.bestReplayLookup.getDistanceByTime(time),
                            currentData.TrackLength,
                            getReferenceTrackLength(context, runtime.bestReplayLookup));
                    })
                },
                GetTimeByDistance: {
                    Invoke: luaMethod(distance => runtime.manager.currentReplayLookup.getTimeByDistance(distance))
                }
            };

            return {
                args: {
                    Graphics: this.graphics,
                    Config: this.globalConfig,
                    I18NLoader: {
                        ResolveByKey: luaFunction(key => resolveBrowserLuaText(key))
                    },
                    GameData: {},
                    GameContext: context,
                    GameOverlayDrawingHelper: {
                        getColor: luaFunction((red, green, blue, alpha) =>
                            new BrowserLuaBrush(red, green, blue, alpha)),
                        getPoint: luaFunction((x, y) => new BrowserLuaPoint(x, y)),
                        getRectangle: luaFunction((left, top, right, bottom) =>
                            new BrowserLuaRectangle(left, top, right, bottom)),
                        drawSector: luaFunction((graphics, unit, x, y, width, height, value, maxValue,
                            backgroundBrush, valueBrush, emptyBrush, textFont, thicknessRatio) =>
                            drawBrowserLuaSector(
                                graphics,
                                unit,
                                x,
                                y,
                                width,
                                height,
                                value,
                                maxValue,
                                backgroundBrush,
                                valueBrush,
                                emptyBrush,
                                textFont,
                                thicknessRatio))
                    },
                    Self: {
                        ImageResources: this.imageResources,
                        GetConfigByKey: luaFunction(key => runtime.settings[toJavaScriptString(key)])
                    },
                    IsWebDashboard: true,
                    WebDashboardStaticLayerDirty: true,
                    NativeStaticLayerEnabled: false,
                    NativeStaticLayerRenderOnly: false,
                    NativeStaticLayerGraphics: null,
                    BrowserTimeText: ''
                }
            };
        }

        createCanvas(kind) {
            const layerStack = this.manager.options.layerStack();
            if (!layerStack) {
                throw new Error('Dashboard canvas is not initialized.');
            }

            const canvas = document.createElement('canvas');
            canvas.className = 'dashboard-render-layer';
            canvas.width = RENDER_WIDTH;
            canvas.height = RENDER_HEIGHT;
            canvas.dataset.dashboardLayer = `browser-lua-${this.id}-${kind}`;
            layerStack.appendChild(canvas);
            return canvas;
        }

        setOrder(order) {
            this.order = Number.isFinite(order) ? order : 0;
            const zIndexBase = this.order * DASHBOARD_LAYER_Z_INDEX_STRIDE;
            this.staticCanvas.style.zIndex = String(zIndexBase);
            this.dynamicCanvas.style.zIndex = String(zIndexBase + 1);
        }

        usesImage(cacheKey) {
            return this.imageCacheKeys.has(cacheKey);
        }

        markStaticDirty() {
            this.staticDirty = true;
        }

        render(frame) {
            const staticContextSignature = JSON.stringify({
                trackName: frame.context.TrackName || '',
                carName: frame.context.CarName || '',
                carClass: frame.context.CarClass || '',
                audioPackage: frame.context.AudioPackage || '',
                pacenoteType: frame.context.PacenoteType || '',
                scriptAuthor: frame.context.ScriptAuthor || ''
            });
            if (staticContextSignature !== this.contextSignature) {
                this.contextSignature = staticContextSignature;
                this.staticDirty = true;
            }

            this.host.args.GameData = frame.data;
            this.host.args.GameData.ToString = luaFunction(() =>
                toJavaScriptString(frame.data.GameDataText));
            this.host.args.GameData.GameSpecificDataToString = luaFunction(() =>
                toJavaScriptString(frame.data.GameSpecificDataText));
            this.host.args.GameData.BrowserTimeText = browserClockText();
            this.host.args.BrowserTimeText = this.host.args.GameData.BrowserTimeText;
            Object.assign(this.host.args.GameContext, frame.context, {
                GetDelta: this.host.args.GameContext.GetDelta,
                GetDeltaByDistanceAndTime: this.host.args.GameContext.GetDeltaByDistanceAndTime,
                GetBestTimeByDistance: this.host.args.GameContext.GetBestTimeByDistance,
                GetBestDistanceByTime: this.host.args.GameContext.GetBestDistanceByTime,
                GetTimeByDistance: this.host.args.GameContext.GetTimeByDistance
            });
            if (frame.context.BestReplayTimeline !== this.bestReplayTimelineSource) {
                this.bestReplayTimelineSource = frame.context.BestReplayTimeline;
                this.bestReplayLookup.replace(frame.context.BestReplayTimeline);
            }
            this.host.args.WebDashboardStaticLayerDirty = this.staticDirty;
            this.graphics.beginFrame(this.staticDirty);
            this.luaState.callGlobal('__ztmz_dashboard_update');
            this.staticDirty = false;
        }

        dispose() {
            try {
                this.luaState?.callGlobal('__ztmz_dashboard_exit', true);
            } catch {
                // A failed dashboard is already leaving the browser runtime.
            }
            this.luaState?.dispose();
            this.luaState = null;
            this.staticCanvas.remove();
            this.dynamicCanvas.remove();
        }
    }

    class BrowserLuaGraphics {
        constructor(runtime) {
            this.runtime = runtime;
            this.Width = RENDER_WIDTH;
            this.Height = RENDER_HEIGHT;
            this.FPS = finiteNumber(
                runtime.globalConfig?.FrameRate ?? runtime.globalConfig?.frameRate,
                60);

            this.BeginStaticLayer = luaFunction(() => this.beginStaticLayer());
            this.EndStaticLayer = luaFunction(() => this.endStaticLayer());
            this.CreateSolidBrush = luaFunction((red, green, blue, alpha) =>
                new BrowserLuaBrush(red, green, blue, alpha));
            this.CreateLinearGradientBrush = luaFunction((...colors) =>
                new BrowserLuaGradientBrush('linear', colors));
            this.CreateRadialGradientBrush = luaFunction((...colors) =>
                new BrowserLuaGradientBrush('radial', colors));
            this.CreateFont = luaFunction((name, size, bold, italic) =>
                new BrowserLuaFont(name, size, bold, italic));
            this.CreateCustomFont = luaFunction((name, size, bold, italic) =>
                new BrowserLuaFont(name, size, bold, italic));
            this.CreateGeometry = luaFunction(() => new BrowserLuaGeometry());
            this.DrawImage = luaFunction((...args) => this.drawImage(...args));
            this.DrawText = luaFunction((font, ...args) => this.drawText(font, ...args));
            this.DrawTextWithBackground = luaFunction((font, brush, background, x, y, text) =>
                this.drawTextWithBackground(font, brush, background, x, y, text));
            this.drawTextWithBackgroundCentered = luaFunction((...args) =>
                this.drawTextWithBackgroundCenteredCore(...args));
            this.FillRectangle = luaFunction((brush, ...args) => this.fillRectangle(brush, ...args));
            this.DrawRectangle = luaFunction((brush, ...args) => this.drawRectangle(brush, ...args));
            this.DrawRoundedRectangle = luaFunction((brush, left, top, right, bottom, radius, thickness) =>
                this.drawRoundedRectangle(brush, left, top, right, bottom, radius, thickness));
            this.DrawLine = luaFunction((brush, ...args) => this.drawLine(brush, ...args));
            this.DrawCircle = luaFunction((brush, x, y, radius, thickness) =>
                this.drawCircle(brush, x, y, radius, thickness));
            this.FillCircle = luaFunction((brush, x, y, radius) => this.fillCircle(brush, x, y, radius));
            this.FillEllipse = luaFunction((brush, x, y, width, height) =>
                this.fillEllipse(brush, x, y, width, height));
            this.FillRoundedRectangle = luaFunction((brush, left, top, right, bottom, radius) =>
                this.fillRoundedRectangle(brush, left, top, right, bottom, radius));
            this.FillGeometry = luaFunction((geometry, brush) => this.fillGeometry(geometry, brush));
            this.DrawGeometry = luaFunction((geometry, brush, thickness) =>
                this.drawGeometry(geometry, brush, thickness));
            this.MeasureString = luaFunction((font, ...args) => this.measureString(font, ...args));
            this.ClipRegionStart = luaFunction((left, top, right, bottom) =>
                this.clipRegionStart(left, top, right, bottom));
            this.ClipRegionEnd = luaFunction(() => this.clipRegionEnd());
        }

        beginFrame(staticDirty) {
            this.runtime.dynamicContext = resetCanvasContext(
                this.runtime.dynamicCanvas,
                this.runtime.dynamicContext);
            this.runtime.activeContext = this.runtime.dynamicContext;
            if (staticDirty) {
                this.runtime.staticContext = resetCanvasContext(
                    this.runtime.staticCanvas,
                    this.runtime.staticContext);
            }
        }

        beginStaticLayer() {
            this.runtime.activeContext = this.runtime.staticContext;
        }

        endStaticLayer() {
            this.runtime.activeContext = this.runtime.dynamicContext;
        }

        drawImage(imageResource, left, top, right, bottom, opacity) {
            if (!imageResource || !imageResource.CacheKey) {
                return;
            }

            const image = this.runtime.manager.options.getImage(imageResource.CacheKey);
            if (!image) {
                this.runtime.manager.options.loadImage(imageResource.CacheKey, imageResource.Url);
                return;
            }

            const ctx = this.runtime.activeContext;
            const alpha = clampOpacity(opacity);
            ctx.save();
            ctx.globalAlpha = alpha;
            if (Number.isFinite(right) && Number.isFinite(bottom)) {
                ctx.drawImage(image, left, top, right - left, bottom - top);
            } else {
                ctx.drawImage(image, left, top);
            }
            ctx.restore();
        }

        drawText(font, ...args) {
            let fontSize;
            let brush;
            let x;
            let y;
            let text;
            if (typeof args[0] === 'number') {
                [fontSize, brush, x, y, text] = args;
            } else {
                [brush, x, y, text] = args;
                fontSize = font?.Size;
            }

            if (!brush || text == null) {
                return;
            }

            const ctx = this.runtime.activeContext;
            ctx.save();
            const resolvedFontSize = resolveFontSize(font, fontSize);
            ctx.font = canvasFont(font, resolvedFontSize);
            ctx.textBaseline = 'alphabetic';
            const layout = measureTextLayout(ctx, text, resolvedFontSize);
            ctx.fillStyle = fillStyleForBrush(ctx, brush, x, y, layout.X, layout.Y);
            drawTextLayout(ctx, layout, x, y);
            ctx.restore();
        }

        drawTextWithBackground(font, brush, background, x, y, text) {
            if (!brush || text == null) {
                return;
            }

            const ctx = this.runtime.activeContext;
            ctx.save();
            const fontSize = resolveFontSize(font, font?.Size);
            ctx.font = canvasFont(font, fontSize);
            ctx.textBaseline = 'alphabetic';
            const layout = measureTextLayout(ctx, text, fontSize);
            if (background && brushOpacity(background) > 0) {
                const padding = fontSize * 0.25;
                ctx.fillStyle = fillStyleForBrush(ctx, background, x, y, layout.X, layout.Y);
                ctx.fillRect(
                    x - padding,
                    y - padding,
                    layout.X + (padding * 2),
                    layout.Y + (padding * 2));
            }
            ctx.fillStyle = fillStyleForBrush(ctx, brush, x, y, layout.X, layout.Y);
            drawTextLayout(ctx, layout, x, y);
            ctx.restore();
        }

        drawTextWithBackgroundCenteredCore(font, fontSize, brush, background, centerX, centerY, text) {
            const ctx = this.runtime.activeContext;
            ctx.save();
            const resolvedFontSize = resolveFontSize(font, fontSize);
            ctx.font = canvasFont(font, resolvedFontSize);
            ctx.textBaseline = 'alphabetic';
            const layout = measureTextLayout(ctx, text, resolvedFontSize);
            const left = centerX - (layout.X / 2);
            const top = centerY - (layout.Y / 2);
            if (brushOpacity(background) > 0) {
                const padding = resolvedFontSize * 0.25;
                ctx.fillStyle = fillStyleForBrush(ctx, background, left, top, layout.X, layout.Y);
                ctx.fillRect(
                    left - padding,
                    top - padding,
                    layout.X + (padding * 2),
                    layout.Y + (padding * 2));
            }
            ctx.fillStyle = fillStyleForBrush(ctx, brush, left, top, layout.X, layout.Y);
            drawTextLayout(ctx, layout, left, top);
            ctx.restore();
        }

        fillCircle(brush, x, y, radius) {
            if (!brush || brushOpacity(brush) <= 0) {
                return;
            }

            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.fillStyle = fillStyleForBrush(ctx, brush, x - radius, y - radius, radius * 2, radius * 2);
            ctx.beginPath();
            ctx.arc(x, y, radius, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
        }

        fillRectangle(brush, ...args) {
            const rectangle = rectangleFromArguments(args);
            if (!rectangle || !brush || brushOpacity(brush) <= 0) {
                return;
            }

            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.fillStyle = fillStyleForBrush(
                ctx,
                brush,
                rectangle.left,
                rectangle.top,
                rectangle.right - rectangle.left,
                rectangle.bottom - rectangle.top);
            ctx.fillRect(
                rectangle.left,
                rectangle.top,
                rectangle.right - rectangle.left,
                rectangle.bottom - rectangle.top);
            ctx.restore();
        }

        drawRectangle(brush, ...args) {
            const thickness = finiteNumber(args[args.length - 1], 1);
            const rectangleArgs = typeof args[0] === 'object' ? args.slice(0, 1) : args.slice(0, 4);
            const rectangle = rectangleFromArguments(rectangleArgs);
            if (!rectangle || !brush || brushOpacity(brush) <= 0) {
                return;
            }

            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.strokeStyle = fillStyleForBrush(
                ctx,
                brush,
                rectangle.left,
                rectangle.top,
                rectangle.right - rectangle.left,
                rectangle.bottom - rectangle.top);
            ctx.lineWidth = Math.max(0.1, thickness);
            ctx.strokeRect(
                rectangle.left,
                rectangle.top,
                rectangle.right - rectangle.left,
                rectangle.bottom - rectangle.top);
            ctx.restore();
        }

        drawRoundedRectangle(brush, left, top, right, bottom, radius, thickness) {
            if (!brush || brushOpacity(brush) <= 0) {
                return;
            }

            const width = right - left;
            const height = bottom - top;
            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.strokeStyle = fillStyleForBrush(ctx, brush, left, top, width, height);
            ctx.lineWidth = Math.max(0.1, finiteNumber(thickness, 1));
            roundedRectanglePath(ctx, left, top, width, height, radius);
            ctx.stroke();
            ctx.restore();
        }

        drawLine(brush, ...args) {
            const line = lineFromArguments(args);
            if (!line || !brush || brushOpacity(brush) <= 0) {
                return;
            }

            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.strokeStyle = fillStyleForBrush(ctx, brush, line.x1, line.y1, line.x2 - line.x1, line.y2 - line.y1);
            ctx.lineWidth = Math.max(0.1, line.thickness);
            ctx.beginPath();
            ctx.moveTo(line.x1, line.y1);
            ctx.lineTo(line.x2, line.y2);
            ctx.stroke();
            ctx.restore();
        }

        drawCircle(brush, x, y, radius, thickness) {
            if (!brush || brushOpacity(brush) <= 0) {
                return;
            }

            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.strokeStyle = fillStyleForBrush(ctx, brush, x - radius, y - radius, radius * 2, radius * 2);
            ctx.lineWidth = Math.max(0.1, finiteNumber(thickness, 1));
            ctx.beginPath();
            ctx.arc(x, y, radius, 0, Math.PI * 2);
            ctx.stroke();
            ctx.restore();
        }

        fillEllipse(brush, x, y, width, height) {
            if (!brush || brushOpacity(brush) <= 0) {
                return;
            }

            const safeWidth = finiteNumber(width, 0);
            const safeHeight = finiteNumber(height, safeWidth);
            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.fillStyle = fillStyleForBrush(ctx, brush, x, y, safeWidth, safeHeight);
            ctx.beginPath();
            ctx.ellipse(
                finiteNumber(x, 0) + (safeWidth / 2),
                finiteNumber(y, 0) + (safeHeight / 2),
                Math.abs(safeWidth / 2),
                Math.abs(safeHeight / 2),
                0,
                0,
                Math.PI * 2);
            ctx.fill();
            ctx.restore();
        }

        fillGeometry(geometry, brush) {
            if (!(geometry instanceof BrowserLuaGeometry) || !brush || brushOpacity(brush) <= 0) {
                return;
            }

            const bounds = geometry.getBounds();
            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.fillStyle = fillStyleForBrush(ctx, brush, bounds.x, bounds.y, bounds.width, bounds.height);
            if (geometry.applyPath(ctx)) {
                ctx.fill();
            }
            ctx.restore();
        }

        drawGeometry(geometry, brush, thickness) {
            if (!(geometry instanceof BrowserLuaGeometry) || !brush || brushOpacity(brush) <= 0) {
                return;
            }

            const bounds = geometry.getBounds();
            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.strokeStyle = fillStyleForBrush(ctx, brush, bounds.x, bounds.y, bounds.width, bounds.height);
            ctx.lineWidth = Math.max(0.1, finiteNumber(thickness, 1));
            if (geometry.applyPath(ctx)) {
                ctx.stroke();
            }
            ctx.restore();
        }

        fillRoundedRectangle(brush, left, top, right, bottom, radius) {
            if (!brush || brushOpacity(brush) <= 0) {
                return;
            }

            const width = right - left;
            const height = bottom - top;
            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.fillStyle = fillStyleForBrush(ctx, brush, left, top, width, height);
            roundedRectanglePath(ctx, left, top, width, height, radius);
            ctx.fill();
            ctx.restore();
        }

        measureString(font, ...args) {
            const fontSize = typeof args[0] === 'number' ? args[0] : font?.Size;
            const text = typeof args[0] === 'number' ? args[1] : args[0];
            const ctx = this.runtime.dynamicContext;
            ctx.save();
            const resolvedFontSize = resolveFontSize(font, fontSize);
            ctx.font = canvasFont(font, resolvedFontSize);
            const layout = measureTextLayout(ctx, text, resolvedFontSize);
            ctx.restore();
            return { X: layout.X, Y: layout.Y };
        }

        clipRegionStart(left, top, right, bottom) {
            const ctx = this.runtime.activeContext;
            ctx.save();
            ctx.beginPath();
            ctx.rect(left, top, right - left, bottom - top);
            ctx.clip();
        }

        clipRegionEnd() {
            this.runtime.activeContext.restore();
        }
    }

    class BrowserLuaGeometry {
        constructor() {
            this.points = [];
            this.currentPoint = null;
            this.isClosed = false;
            this.figureStarted = false;
            this.BeginFigure = luaFunction((point, filled) => this.beginFigure(point, filled));
            this.AddPoint = luaFunction(point => this.addPoint(point));
            this.AddLine = luaFunction(point => this.addPoint(point));
            this.AddCurve = luaFunction((point, radius, arcSize, sweepDirection) =>
                this.addCurve(point, radius, arcSize, sweepDirection));
            this.AddCurveWithArcSegmentArgs = luaFunction((point, radius, arcSize, sweepDirection) =>
                this.addCurve(point, radius, arcSize, sweepDirection));
            this.EndFigure = luaFunction(() => {
                this.figureStarted = false;
            });
            this.Close = luaFunction(() => {
                this.isClosed = true;
                this.figureStarted = false;
            });
            this.Dispose = () => {};
        }

        beginFigure(point) {
            const parsedPoint = pointFrom(point);
            if (!parsedPoint) {
                throw new Error('Geometry requires a valid start point.');
            }

            this.points = [parsedPoint];
            this.currentPoint = parsedPoint;
            this.figureStarted = true;
            this.isClosed = false;
        }

        addPoint(point) {
            const parsedPoint = pointFrom(point);
            if (!parsedPoint) {
                throw new Error('Geometry requires a valid point.');
            }

            if (!this.figureStarted || !this.currentPoint) {
                this.beginFigure(parsedPoint);
                return;
            }

            this.points.push(parsedPoint);
            this.currentPoint = parsedPoint;
        }

        addCurve(point, radius, arcSize = 1, sweepDirection = 1) {
            const endPoint = pointFrom(point);
            if (!endPoint) {
                throw new Error('Geometry requires a valid arc end point.');
            }

            if (!this.figureStarted || !this.currentPoint) {
                this.beginFigure(endPoint);
                return;
            }

            const startPoint = this.currentPoint;
            const safeRadius = Math.abs(finiteNumber(radius, 0));
            const deltaX = endPoint.x - startPoint.x;
            const deltaY = endPoint.y - startPoint.y;
            const distance = Math.hypot(deltaX, deltaY);
            if (distance < 0.01 || safeRadius < 0.01 || distance > safeRadius * 2) {
                this.points.push(endPoint);
                this.currentPoint = endPoint;
                return;
            }

            const midpointX = (startPoint.x + endPoint.x) / 2;
            const midpointY = (startPoint.y + endPoint.y) / 2;
            const halfChord = distance / 2;
            const centerOffset = Math.sqrt(Math.max(0, (safeRadius * safeRadius) - (halfChord * halfChord)));
            const perpendicularX = deltaY / distance;
            const perpendicularY = -deltaX / distance;
            const isLargeArc = Number(arcSize) !== 0;
            const isClockwise = Number(sweepDirection) !== 0;
            const usePositivePerpendicular = isClockwise === isLargeArc;
            const centerX = midpointX + (usePositivePerpendicular ? centerOffset : -centerOffset) * perpendicularX;
            const centerY = midpointY + (usePositivePerpendicular ? centerOffset : -centerOffset) * perpendicularY;
            const startAngle = Math.atan2(startPoint.y - centerY, startPoint.x - centerX);
            const endAngle = Math.atan2(endPoint.y - centerY, endPoint.x - centerX);
            let sweepAngle = endAngle - startAngle;
            if (isClockwise) {
                if (sweepAngle < 0) {
                    sweepAngle += Math.PI * 2;
                }
            } else if (sweepAngle > 0) {
                sweepAngle -= Math.PI * 2;
            }

            const segments = 20;
            for (let index = 1; index <= segments; index++) {
                const angle = startAngle + (sweepAngle * index / segments);
                this.points.push({
                    x: centerX + (safeRadius * Math.cos(angle)),
                    y: centerY + (safeRadius * Math.sin(angle))
                });
            }
            this.points[this.points.length - 1] = endPoint;
            this.currentPoint = endPoint;
        }

        applyPath(ctx) {
            if (this.points.length === 0) {
                return false;
            }

            ctx.beginPath();
            ctx.moveTo(this.points[0].x, this.points[0].y);
            for (let index = 1; index < this.points.length; index++) {
                ctx.lineTo(this.points[index].x, this.points[index].y);
            }
            if (this.isClosed) {
                ctx.closePath();
            }
            return true;
        }

        getBounds() {
            if (this.points.length === 0) {
                return { x: 0, y: 0, width: 0, height: 0 };
            }

            let minX = this.points[0].x;
            let maxX = minX;
            let minY = this.points[0].y;
            let maxY = minY;
            for (const point of this.points) {
                minX = Math.min(minX, point.x);
                maxX = Math.max(maxX, point.x);
                minY = Math.min(minY, point.y);
                maxY = Math.max(maxY, point.y);
            }
            return { x: minX, y: minY, width: maxX - minX, height: maxY - minY };
        }
    }

    class BrowserLuaReplayLookup {
        constructor(options = {}) {
            this.maximumSampleCount = Math.max(
                0,
                Math.floor(finiteNumber(options.maximumSampleCount, 0)));
            this.minimumTimeDelta = Math.max(0, finiteNumber(options.minimumTimeDelta, 0));
            this.minimumDistanceDelta = Math.max(0, finiteNumber(options.minimumDistanceDelta, 0));
            this.samples = [];
            this.byTime = [];
            this.byDistance = [];
        }

        replace(samples) {
            this.samples = Array.isArray(samples)
                ? samples
                    .map(sample => ({
                        time: finiteNumber(sample?.Time ?? sample?.time, NaN),
                        distance: finiteNumber(sample?.Distance ?? sample?.distance, NaN)
                    }))
                    .filter(sample => Number.isFinite(sample.time) && Number.isFinite(sample.distance))
                : [];
            this.rebuildIndexes();
        }

        record(time, distance) {
            const sampleTime = finiteNumber(time, NaN);
            const sampleDistance = finiteNumber(distance, NaN);
            if (!Number.isFinite(sampleTime) || !Number.isFinite(sampleDistance)) {
                return;
            }

            let last = this.samples[this.samples.length - 1];
            if (last && sampleTime < last.time - 0.25) {
                this.clear();
                last = null;
            }

            if (last
                && sampleTime - last.time < this.minimumTimeDelta
                && Math.abs(sampleDistance - last.distance) < this.minimumDistanceDelta) {
                return;
            }

            const sample = { time: sampleTime, distance: sampleDistance };
            this.samples.push(sample);
            this.insertIntoIndex(this.byTime, sample, 'time');
            this.insertIntoIndex(this.byDistance, sample, 'distance');
            this.compactIfNeeded();
        }

        getTimeByDistance(distance) {
            return interpolateReplaySamples(this.getByDistance(), finiteNumber(distance, 0), 'distance', 'time');
        }

        getDistanceByTime(time) {
            return interpolateReplaySamples(this.getByTime(), finiteNumber(time, 0), 'time', 'distance');
        }

        getByTime() {
            return this.byTime;
        }

        getByDistance() {
            return this.byDistance;
        }

        clear() {
            this.samples = [];
            this.byTime = [];
            this.byDistance = [];
        }

        rebuildIndexes() {
            this.byTime = [...this.samples].sort((left, right) => left.time - right.time);
            this.byDistance = [...this.samples].sort((left, right) => left.distance - right.distance);
        }

        insertIntoIndex(index, sample, key) {
            const last = index[index.length - 1];
            if (!last || sample[key] >= last[key]) {
                index.push(sample);
                return;
            }

            let low = 0;
            let high = index.length;
            while (low < high) {
                const middle = Math.floor((low + high) / 2);
                if (index[middle][key] <= sample[key]) {
                    low = middle + 1;
                } else {
                    high = middle;
                }
            }
            index.splice(low, 0, sample);
        }

        compactIfNeeded() {
            if (this.maximumSampleCount === 0 || this.samples.length <= this.maximumSampleCount) {
                return;
            }

            const targetCount = Math.max(1, Math.floor(this.maximumSampleCount * 0.75));
            const recentCount = Math.min(Math.floor(targetCount * 0.25), this.samples.length);
            const historyEnd = this.samples.length - recentCount;
            const historyCount = Math.max(1, targetCount - recentCount);
            const retainedHistoryCount = Math.min(historyCount, historyEnd);
            const compacted = [];

            for (let index = 0; index < retainedHistoryCount; index++) {
                const sourceIndex = retainedHistoryCount === 1
                    ? 0
                    : Math.round(index * (historyEnd - 1) / (retainedHistoryCount - 1));
                compacted.push(this.samples[sourceIndex]);
            }

            compacted.push(...this.samples.slice(historyEnd));
            this.samples = compacted;
            this.rebuildIndexes();
        }
    }

    class BrowserLuaState {
        constructor(host) {
            const fengari = window.fengari;
            this.lua = fengari.lua;
            this.lauxlib = fengari.lauxlib;
            this.interop = fengari.interop;
            this.toLuaString = fengari.to_luastring;
            this.L = this.lauxlib.luaL_newstate();
            fengari.lualib.luaL_openlibs(this.L);
            this.lauxlib.luaL_requiref(
                this.L,
                this.toLuaString('js'),
                this.interop.luaopen_js,
                1);
            this.lua.lua_pop(this.L, 1);
            this.interop.push(this.L, host);
            this.lua.lua_setglobal(this.L, this.toLuaString('__ztmz_dashboard_host'));

            for (const name of ['js', 'debug', 'dofile', 'io', 'load', 'loadfile', 'os', 'package', 'require']) {
                this.lua.lua_pushnil(this.L);
                this.lua.lua_setglobal(this.L, this.toLuaString(name));
            }
        }

        runChunk(source, chunkName) {
            const loadStatus = this.lauxlib.luaL_loadbuffer(
                this.L,
                this.toLuaString(String(source || '')),
                null,
                this.toLuaString(chunkName));
            if (loadStatus !== this.lua.LUA_OK) {
                throw this.createLuaError(chunkName);
            }

            const callStatus = this.lua.lua_pcall(this.L, 0, 0, 0);
            if (callStatus !== this.lua.LUA_OK) {
                throw this.createLuaError(chunkName);
            }
        }

        callGlobal(name, ignoreMissing) {
            this.lua.lua_getglobal(this.L, this.toLuaString(name));
            if (!this.lua.lua_isfunction(this.L, -1)) {
                this.lua.lua_pop(this.L, 1);
                if (ignoreMissing) {
                    return;
                }
                throw new Error(`Lua dashboard does not define ${name}.`);
            }

            const callStatus = this.lua.lua_pcall(this.L, 0, 0, 0);
            if (callStatus !== this.lua.LUA_OK) {
                throw this.createLuaError(name);
            }
        }

        dispose() {
            if (this.L) {
                this.lua.lua_close(this.L);
                this.L = null;
            }
        }

        createLuaError(scope) {
            let errorValue;
            try {
                errorValue = this.interop.tojs(this.L, -1);
            } catch {
                errorValue = undefined;
            }
            const errorMessage = errorValue && typeof errorValue.message === 'string'
                ? errorValue.message
                : null;
            const message = errorMessage != null
                ? errorMessage
                : errorValue == null
                    ? this.lua.lua_tojsstring(this.L, -1)
                    : String(errorValue);
            this.lua.lua_pop(this.L, 1);
            const wrappedError = new Error(`${scope}: ${message}`);
            if (errorValue && typeof errorValue.stack === 'string') {
                wrappedError.stack = `${wrappedError.stack}\nCaused by: ${errorValue.stack}`;
            }
            return wrappedError;
        }
    }

    class BrowserLuaPoint {
        constructor(x, y) {
            this.X = finiteNumber(x, 0);
            this.Y = finiteNumber(y, 0);
        }
    }

    class BrowserLuaRectangle {
        constructor(left, top, right, bottom) {
            this.Left = finiteNumber(left, 0);
            this.Top = finiteNumber(top, 0);
            this.Right = finiteNumber(right, this.Left);
            this.Bottom = finiteNumber(bottom, this.Top);
        }
    }

    class BrowserLuaImage {
        constructor(manager, cacheKey, url) {
            this.manager = manager;
            this.CacheKey = cacheKey;
            this.Url = url;
            this.Dispose = () => {};
        }

        get Width() {
            const image = this.manager.options.getImage(this.CacheKey);
            return finiteNumber(image?.naturalWidth ?? image?.videoWidth ?? image?.width, 0);
        }

        get Height() {
            const image = this.manager.options.getImage(this.CacheKey);
            return finiteNumber(image?.naturalHeight ?? image?.videoHeight ?? image?.height, 0);
        }
    }

    class BrowserLuaColor {
        constructor(red, green, blue, alpha) {
            this.R = clampColor(red);
            this.G = clampColor(green);
            this.B = clampColor(blue);
            this.A = clampColor(alpha == null ? 255 : alpha);
        }
    }

    class BrowserLuaBrush {
        constructor(red, green, blue, alpha) {
            this.Red = clampColor(red);
            this.Green = clampColor(green);
            this.Blue = clampColor(blue);
            this.Alpha = clampColor(alpha == null ? 255 : alpha);
            Object.defineProperty(this, 'Color', {
                enumerable: true,
                get: () => new BrowserLuaColor(this.Red, this.Green, this.Blue, this.Alpha),
                set: value => {
                    this.Red = readColorComponent(value, ['R', 'Red'], this.Red);
                    this.Green = readColorComponent(value, ['G', 'Green'], this.Green);
                    this.Blue = readColorComponent(value, ['B', 'Blue'], this.Blue);
                    this.Alpha = readColorComponent(value, ['A', 'Alpha'], this.Alpha);
                }
            });
            this.Dispose = () => {};
        }

        toCss() {
            return `rgba(${this.Red}, ${this.Green}, ${this.Blue}, ${this.Alpha / 255})`;
        }
    }

    class BrowserLuaGradientBrush {
        constructor(kind, colors) {
            this.Kind = kind;
            this.Colors = colors.filter(color => color instanceof BrowserLuaBrush);
            this.StartX = 0;
            this.StartY = 0;
            this.EndX = 1;
            this.EndY = 1;
            this.CenterX = 0;
            this.CenterY = 0;
            this.RadiusX = 1;
            this.RadiusY = 1;
            this.SetRange = luaFunction((startX, startY, endX, endY) => {
                this.StartX = startX;
                this.StartY = startY;
                this.EndX = endX;
                this.EndY = endY;
            });
            this.SetCenter = luaFunction((centerX, centerY) => {
                this.CenterX = centerX;
                this.CenterY = centerY;
            });
            this.SetRadius = luaFunction((radiusX, radiusY) => {
                this.RadiusX = radiusX;
                this.RadiusY = radiusY;
            });
            this.Dispose = () => {};
        }

        createStyle(ctx, x, y, width, height) {
            if (this.Colors.length === 0) {
                return 'rgba(0, 0, 0, 0)';
            }

            let gradient;
            if (this.Kind === 'radial') {
                const centerX = Number.isFinite(this.CenterX) ? this.CenterX : x + (width / 2);
                const centerY = Number.isFinite(this.CenterY) ? this.CenterY : y + (height / 2);
                const radius = Math.max(1, Number.isFinite(this.RadiusX) ? this.RadiusX : Math.max(width, height) / 2);
                gradient = ctx.createRadialGradient(centerX, centerY, 0, centerX, centerY, radius);
            } else {
                const startX = Number.isFinite(this.StartX) ? this.StartX : x;
                const startY = Number.isFinite(this.StartY) ? this.StartY : y;
                const endX = Number.isFinite(this.EndX) ? this.EndX : x + width;
                const endY = Number.isFinite(this.EndY) ? this.EndY : y + height;
                gradient = ctx.createLinearGradient(startX, startY, endX, endY);
            }

            const denominator = Math.max(1, this.Colors.length - 1);
            this.Colors.forEach((color, index) => gradient.addColorStop(index / denominator, color.toCss()));
            return gradient;
        }
    }

    class BrowserLuaFont {
        constructor(name, size, bold, italic) {
            this.Name = toJavaScriptString(name || 'Arial');
            this.Size = finiteNumber(size, 14);
            this.FontSize = this.Size;
            this.Bold = Boolean(bold);
            this.Italic = Boolean(italic);
            this.Dispose = () => {};
        }
    }

    function resetCanvasContext(canvas, ctx) {
        if (typeof ctx.reset === 'function') {
            ctx.reset();
            return ctx;
        }

        // Resizing is the only standards-compatible way to discard an unknown
        // clip or transform state on older browsers.
        canvas.width = canvas.width;
        return canvas.getContext('2d');
    }

    function readDashboardDescriptorValue(descriptor, camelCaseName) {
        if (!descriptor) {
            return undefined;
        }

        if (descriptor[camelCaseName] !== undefined) {
            return descriptor[camelCaseName];
        }

        const pascalCaseName = `${camelCaseName.charAt(0).toUpperCase()}${camelCaseName.substring(1)}`;
        return descriptor[pascalCaseName];
    }

    function drawBrowserLuaSector(
        graphics,
        unit,
        x,
        y,
        width,
        height,
        value,
        maxValue,
        backgroundBrush,
        valueBrush,
        emptyBrush,
        textFont,
        thicknessRatio) {
        if (!(graphics instanceof BrowserLuaGraphics)) {
            return new BrowserLuaPoint(x, y);
        }

        const safeMaxValue = Math.max(0.0001, finiteNumber(maxValue, 1));
        const safeValue = Math.max(0, Math.min(finiteNumber(value, 0), safeMaxValue));
        const centerX = finiteNumber(x, 0) + (finiteNumber(width, 0) / 2);
        const centerY = finiteNumber(y, 0) + (finiteNumber(height, 0) / 2);
        let radiusOuter = Math.max(0, Math.min(finiteNumber(width, 0), finiteNumber(height, 0)) / 2);
        let radiusInner = radiusOuter * (1 - Math.max(0, Math.min(1, finiteNumber(thicknessRatio, 0.2))));
        const startAngle = Math.PI * 1.25;
        const endAngle = Math.PI * 1.75;
        const outerStart = pointOnSector(centerX, centerY, radiusOuter, startAngle);
        const outerEnd = pointOnSector(centerX, centerY, radiusOuter, endAngle);
        const innerEnd = pointOnSector(centerX, centerY, radiusInner, endAngle);
        const innerStart = pointOnSector(centerX, centerY, radiusInner, startAngle);

        const backgroundGeometry = new BrowserLuaGeometry();
        backgroundGeometry.beginFigure(outerStart, true);
        backgroundGeometry.addCurve(outerEnd, radiusOuter, 1, 1);
        backgroundGeometry.addPoint(innerEnd);
        backgroundGeometry.addCurve(innerStart, radiusInner, 1, 0);
        backgroundGeometry.isClosed = true;
        graphics.fillGeometry(backgroundGeometry, emptyBrush);
        graphics.drawGeometry(backgroundGeometry, backgroundBrush, 1);

        const ratio = safeValue / safeMaxValue;
        const valueAngle = startAngle - (ratio * Math.PI * 1.5);
        const arcSize = ratio * 270 >= 180 ? 1 : 0;
        radiusOuter = Math.max(0, radiusOuter - 1);
        radiusInner += 1;
        const valueOuterStart = pointOnSector(centerX, centerY, radiusOuter, startAngle);
        const valueOuterEnd = pointOnSector(centerX, centerY, radiusOuter, valueAngle);
        const valueInnerEnd = pointOnSector(centerX, centerY, radiusInner, valueAngle);
        const valueInnerStart = pointOnSector(centerX, centerY, radiusInner, startAngle);
        const valueGeometry = new BrowserLuaGeometry();
        valueGeometry.beginFigure(valueOuterStart, true);
        valueGeometry.addCurve(valueOuterEnd, radiusOuter, arcSize, 1);
        valueGeometry.addPoint(valueInnerEnd);
        valueGeometry.addCurve(valueInnerStart, radiusInner, arcSize, 0);
        valueGeometry.isClosed = true;
        graphics.fillGeometry(valueGeometry, valueBrush);
        graphics.drawTextWithBackgroundCenteredCore(
            textFont,
            0.2 * radiusOuter,
            valueBrush,
            backgroundBrush,
            centerX,
            centerY + radiusInner,
            unit);
        graphics.drawTextWithBackgroundCenteredCore(
            textFont,
            0.45 * radiusOuter,
            valueBrush,
            backgroundBrush,
            centerX,
            centerY,
            String(Math.trunc(safeValue)));
        return new BrowserLuaPoint(valueInnerStart.X, valueInnerStart.Y);
    }

    function pointOnSector(centerX, centerY, radius, angle) {
        return new BrowserLuaPoint(
            centerX + (radius * Math.cos(angle)),
            centerY - (radius * Math.sin(angle)));
    }

    function interpolateReplaySamples(samples, target, inputKey, outputKey) {
        if (!Array.isArray(samples) || samples.length === 0) {
            return 0;
        }

        if (target <= samples[0][inputKey]) {
            return finiteNumber(samples[0][outputKey], 0);
        }

        const last = samples[samples.length - 1];
        if (target >= last[inputKey]) {
            return finiteNumber(last[outputKey], 0);
        }

        let low = 0;
        let high = samples.length - 1;
        while (low <= high) {
            const middle = Math.floor((low + high) / 2);
            if (samples[middle][inputKey] < target) {
                low = middle + 1;
            } else {
                high = middle - 1;
            }
        }

        const right = samples[Math.min(samples.length - 1, low)];
        const left = samples[Math.max(0, low - 1)];
        const range = right[inputKey] - left[inputKey];
        if (!Number.isFinite(range) || Math.abs(range) < 0.00001) {
            return finiteNumber(right[outputKey], finiteNumber(left[outputKey], 0));
        }

        const ratio = (target - left[inputKey]) / range;
        return finiteNumber(
            left[outputKey] + ((right[outputKey] - left[outputKey]) * ratio),
            0);
    }

    function resolveBrowserLuaText(key) {
        const textKey = toJavaScriptString(key);
        const labels = {
            'ui.tb_gamestate': 'Game state',
            'overlay.track': 'Track',
            'overlay.car': 'Car',
            'overlay.carclass': 'Class',
            'overlay.audioPackage': 'Audio package',
            'overlay.scriptAuthor': 'Script author'
        };
        return labels[textKey] || textKey;
    }

    function browserClockText() {
        const now = new Date();
        return [now.getHours(), now.getMinutes(), now.getSeconds()]
            .map(value => String(value).padStart(2, '0'))
            .join(':');
    }

    function roundedRectanglePath(ctx, x, y, width, height, radius) {
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

    function rectangleFromArguments(args) {
        if (!args || args.length === 0) {
            return null;
        }

        const rectangle = args[0];
        if (rectangle && typeof rectangle === 'object') {
            const left = finiteNumber(rectangle.Left ?? rectangle.left ?? rectangle.X ?? rectangle.x, NaN);
            const top = finiteNumber(rectangle.Top ?? rectangle.top ?? rectangle.Y ?? rectangle.y, NaN);
            const right = finiteNumber(rectangle.Right ?? rectangle.right, NaN);
            const bottom = finiteNumber(rectangle.Bottom ?? rectangle.bottom, NaN);
            if (Number.isFinite(left) && Number.isFinite(top)
                && Number.isFinite(right) && Number.isFinite(bottom)) {
                return { left, top, right, bottom };
            }
        }

        const [left, top, right, bottom] = args.map(value => finiteNumber(value, NaN));
        return Number.isFinite(left) && Number.isFinite(top)
            && Number.isFinite(right) && Number.isFinite(bottom)
            ? { left, top, right, bottom }
            : null;
    }

    function lineFromArguments(args) {
        if (!args || args.length === 0) {
            return null;
        }

        if (args[0] && typeof args[0] === 'object'
            && args[1] && typeof args[1] === 'object') {
            const first = pointFrom(args[0]);
            const second = pointFrom(args[1]);
            const thickness = finiteNumber(args[2], 1);
            return first && second
                ? { x1: first.x, y1: first.y, x2: second.x, y2: second.y, thickness }
                : null;
        }

        const [x1, y1, x2, y2, thickness = 1] = args.map(value => finiteNumber(value, NaN));
        return Number.isFinite(x1) && Number.isFinite(y1)
            && Number.isFinite(x2) && Number.isFinite(y2)
            ? { x1, y1, x2, y2, thickness: Number.isFinite(thickness) ? thickness : 1 }
            : null;
    }

    function pointFrom(point) {
        const x = finiteNumber(point?.X ?? point?.x, NaN);
        const y = finiteNumber(point?.Y ?? point?.y, NaN);
        return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
    }

    function readColorComponent(value, keys, fallback) {
        if (!value || typeof value !== 'object') {
            return fallback;
        }

        for (const key of keys) {
            if (value[key] !== undefined) {
                return clampColor(value[key]);
            }
        }
        return fallback;
    }

    function canvasFont(font, fontSize) {
        const size = resolveFontSize(font, fontSize);
        const style = font?.Italic ? 'italic' : 'normal';
        const weight = font?.Bold ? 700 : 400;
        const name = String(font?.Name || 'Arial').replace(/\\/g, '\\\\').replace(/"/g, '\\"');
        return `${style} ${weight} ${size}px "${name}", Arial, sans-serif`;
    }

    function resolveFontSize(font, fontSize) {
        return Math.max(1, finiteNumber(fontSize, finiteNumber(font?.Size, 14)));
    }

    function measureTextLayout(ctx, text, fontSize) {
        const lines = splitTextLines(text);
        const size = Math.max(1, finiteNumber(fontSize, 14));
        const referenceMetrics = ctx.measureText('Mg');
        const fontBoundingAscent = finiteNumber(referenceMetrics.fontBoundingBoxAscent, 0);
        const fontBoundingDescent = finiteNumber(referenceMetrics.fontBoundingBoxDescent, 0);
        const actualAscent = finiteNumber(referenceMetrics.actualBoundingBoxAscent, 0);
        const actualDescent = finiteNumber(referenceMetrics.actualBoundingBoxDescent, 0);
        const baseline = fontBoundingAscent > 0
            ? fontBoundingAscent
            : actualAscent > 0 ? actualAscent : size * 0.8;
        const descent = fontBoundingDescent > 0
            ? fontBoundingDescent
            : actualDescent > 0 ? actualDescent : size * 0.2;
        const lineHeight = Math.max(1, baseline + descent);
        let width = 0;

        for (const line of lines) {
            width = Math.max(width, finiteNumber(ctx.measureText(line).width, 0));
        }

        return {
            lines,
            X: width,
            Y: lineHeight * lines.length,
            baseline,
            lineHeight
        };
    }

    function drawTextLayout(ctx, layout, x, y) {
        for (let index = 0; index < layout.lines.length; index++) {
            ctx.fillText(
                layout.lines[index],
                x,
                y + layout.baseline + (index * layout.lineHeight));
        }
    }

    function splitTextLines(text) {
        return toJavaScriptString(text).replace(/\r\n?/g, '\n').split('\n');
    }

    function fillStyleForBrush(ctx, brush, x, y, width, height) {
        if (brush instanceof BrowserLuaGradientBrush) {
            return brush.createStyle(ctx, x, y, width, height);
        }
        if (brush instanceof BrowserLuaBrush) {
            return brush.toCss();
        }
        return 'rgba(0, 0, 0, 0)';
    }

    function brushOpacity(brush) {
        return brush instanceof BrowserLuaBrush ? brush.Alpha : 255;
    }

    function clampColor(value) {
        const number = Number(value);
        return Number.isFinite(number) ? Math.max(0, Math.min(255, Math.round(number))) : 0;
    }

    function clampOpacity(value) {
        if (value == null) {
            return 1;
        }
        const number = Number(value);
        return Number.isFinite(number) ? Math.max(0, Math.min(1, number)) : 1;
    }

    function finiteNumber(value, fallback) {
        const number = Number(value);
        return Number.isFinite(number) ? number : fallback;
    }

    function getReferenceTrackLength(context, replayLookup) {
        const configuredLength = finiteNumber(
            context?.LocalReplay?.trackLength ?? context?.LocalReplay?.TrackLength,
            0);
        if (configuredLength > 0.01) {
            return configuredLength;
        }

        const samples = replayLookup?.getByDistance?.() || [];
        return samples.length > 0 ? finiteNumber(samples[samples.length - 1].distance, 0) : 0;
    }

    function mapCurrentDistanceToReferenceDistance(distance, currentTrackLength, referenceTrackLength) {
        const currentDistance = finiteNumber(distance, 0);
        const currentLength = finiteNumber(currentTrackLength, 0);
        const referenceLength = finiteNumber(referenceTrackLength, 0);
        if (currentLength <= 0.01 || referenceLength <= 0.01) {
            return currentDistance;
        }

        return Math.max(0, Math.min(1, currentDistance / currentLength)) * referenceLength;
    }

    function mapReferenceDistanceToCurrentDistance(distance, currentTrackLength, referenceTrackLength) {
        const referenceDistance = finiteNumber(distance, 0);
        const currentLength = finiteNumber(currentTrackLength, 0);
        const referenceLength = finiteNumber(referenceTrackLength, 0);
        if (currentLength <= 0.01 || referenceLength <= 0.01) {
            return referenceDistance;
        }

        return Math.max(0, Math.min(1, referenceDistance / referenceLength)) * currentLength;
    }

    // Fengari passes the first Lua function argument as JavaScript `this`.
    // The maintained dashboards use dot calls, so restore that first argument
    // before invoking the normal JavaScript implementation.
    function luaFunction(callback) {
        return function browserLuaInteropFunction(...args) {
            return callback(this, ...args);
        };
    }

    // Fengari supplies the object receiver as JavaScript `this` for Lua colon
    // calls, while the actual Lua arguments remain in `args`.
    function luaMethod(callback) {
        return function browserLuaInteropMethod(...args) {
            return callback(...args);
        };
    }

    function toJavaScriptString(value) {
        if (typeof value === 'string') {
            return value;
        }

        if (value != null
            && typeof window.fengari?.to_jsstring === 'function'
            && (value instanceof Uint8Array || ArrayBuffer.isView(value))) {
            try {
                return window.fengari.to_jsstring(value);
            } catch {
                // Fall through to JavaScript's normal string conversion.
            }
        }

        return value == null ? '' : String(value);
    }

    window.ZtmzBrowserLuaDashboardManager = BrowserLuaDashboardManager;
}());
