const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../../src/ZTMZ.PacenoteTool.WebDashboard/wwwroot/screen-awake.js'), 'utf8');

function fixture(request, play = () => Promise.resolve()) {
    const window = { addEventListener() {} };
    vm.runInNewContext(script, { window });
    const videos = [];
    const document = {
        visibilityState: 'visible', body: { appendChild() {} },
        createElement(tag) {
            const listeners = {};
            const element = {
                style: {}, setAttribute() {}, appendChild() {},
                addEventListener(name, fn) { listeners[name] = fn; },
                play, pause() { this.paused = true; listeners.pause?.(); },
                remove() { this.removed = true; }
            };
            if (tag === 'video') videos.push(element);
            return element;
        }
    };
    const controller = new window.DashboardScreenAwake({
        document, navigator: request ? { wakeLock: { request } } : {}, secure: !!request,
        media: { mp4: 'local', webm: 'local' }, notify() {}
    });
    return { controller, videos, document };
}

function sentinel() {
    return { released: false, addEventListener(_, fn) { this.onRelease = fn; },
        release() { this.released = true; this.onRelease?.(); return Promise.resolve(); } };
}

test('native lock preferred; release on disable', async () => {
    const lock = sentinel();
    const { controller, videos } = fixture(async () => lock);
    await controller.enable();
    assert.equal(controller.status, 'native');
    assert.equal(videos.length, 0);
    controller.disable();
    assert.equal(lock.released, true);
    assert.equal(controller.status, 'off');
});

test('pending native request cannot resurrect disabled lock', async () => {
    let resolve;
    const { controller } = fixture(() => new Promise(r => resolve = r));
    const pending = controller.enable();
    controller.disable();
    const lock = sentinel(); resolve(lock); await pending;
    assert.equal(lock.released, true);
    assert.equal(controller.status, 'off');
});

test('HTTP video starts on gesture and stops when hidden', async () => {
    let played = false;
    const { controller, videos, document } = fixture(null, () => { played = true; return Promise.resolve(); });
    const pending = controller.enable();
    assert.equal(played, true);
    await pending;
    assert.equal(controller.status, 'video');
    document.visibilityState = 'hidden'; controller.suspend();
    assert.equal(videos[0].paused, true);
    assert.equal(videos[0].removed, true);
    assert.equal(controller.status, 'paused');
    document.visibilityState = 'visible'; await controller.enable();
    assert.equal(controller.status, 'video');
    controller.disable();
});

test('native denial falls back; autoplay denial reports retry', async () => {
    const { controller, videos } = fixture(async () => { throw Error('denied'); }, () => Promise.reject(Error('gesture')));
    await controller.enable();
    assert.equal(controller.status, 'retry');
    assert.equal(videos[0].removed, true);
    assert.equal(controller.videoOnly, true);
});

test('native lock resumes after background; system release does not loop', async () => {
    let calls = 0;
    const { controller, document } = fixture(async () => { calls++; return sentinel(); });
    await controller.enable();
    document.visibilityState = 'hidden'; controller.suspend();
    assert.equal(controller.lock, null);
    document.visibilityState = 'visible'; await controller.enable();
    assert.equal(calls, 2);
    await controller.lock.release();
    assert.equal(controller.status, 'retry');
    assert.equal(calls, 2);
});

test('video play completion after disable does not show enabled', async () => {
    let resolve;
    const { controller, videos } = fixture(null, () => new Promise(r => resolve = r));
    const pending = controller.enable();
    controller.disable(); resolve(); await pending;
    assert.equal(controller.status, 'off');
    assert.equal(videos[0].removed, true);
});
