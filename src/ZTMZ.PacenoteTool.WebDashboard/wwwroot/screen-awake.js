/* Screen wake lock with a local, best-effort video fallback for LAN HTTP. */
class DashboardScreenAwake {
    constructor({ document, navigator, secure, media, notify }) {
        Object.assign(this, { document, navigator, secure, media, notify });
        this.enabled = false;
        this.status = 'off';
        this.generation = 0;
        this.lock = null;
        this.video = null;
        this.videoOnly = false;
    }

    report(status) {
        this.status = status;
        this.notify(status);
    }

    stopResources() {
        const lock = this.lock;
        this.lock = null;
        if (lock) Promise.resolve(lock.release()).catch(() => {});
        if (this.video) {
            const video = this.video;
            this.video = null;
            video.pause();
            video.remove();
        }
    }

    disable() {
        this.enabled = false;
        ++this.generation;
        this.stopResources();
        this.report('off');
    }

    suspend() {
        ++this.generation;
        this.stopResources();
        if (this.enabled) this.report('paused');
    }

    async enable() {
        this.enabled = true;
        const generation = ++this.generation;
        this.stopResources();
        if (this.document.visibilityState !== 'visible') {
            this.report('paused');
            return;
        }
        const current = () => this.enabled && this.generation === generation;
        this.report('pending');
        if (this.secure && this.navigator.wakeLock && !this.videoOnly) {
            try {
                const lock = await this.navigator.wakeLock.request('screen');
                if (!current()) {
                    await lock.release();
                    return;
                }
                this.lock = lock;
                lock.addEventListener('release', () => {
                    if (this.lock !== lock) return;
                    this.lock = null;
                    // Do not repeatedly fight a system power-saving decision.
                    this.report(this.document.visibilityState === 'visible' ? 'retry' : 'paused');
                });
                this.report('native');
                return;
            } catch {
                if (!current()) return;
                // If autoplay now needs a fresh gesture, the retry button starts
                // the video directly on the next click, without another await.
                this.videoOnly = true;
            }
        }
        try {
            if (!this.media) throw new Error('Missing wake video resources');
            const video = this.document.createElement('video');
            this.video = video;
            video.muted = true;
            video.defaultMuted = true;
            video.setAttribute('playsinline', '');
            video.setAttribute('aria-hidden', 'true');
            video.tabIndex = -1;
            video.style.cssText = 'position:fixed;width:1px;height:1px;bottom:0;right:0;opacity:0.01;pointer-events:none';
            for (const format of ['webm', 'mp4']) {
                const source = this.document.createElement('source');
                source.src = this.media[format];
                source.type = `video/${format}`;
                video.appendChild(source);
            }
            // The small NoSleep MP4 uses timeline advancement, rather than a
            // static/empty video that mobile browsers may suspend.
            video.addEventListener('loadedmetadata', () => {
                if (video.duration <= 1) video.loop = true;
            });
            video.addEventListener('timeupdate', () => {
                if (video.duration > 1 && video.currentTime > 0.5) video.currentTime = Math.random() * 0.5;
            });
            const interrupted = () => {
                if (!current() || this.video !== video) return;
                this.stopResources();
                this.report('retry');
            };
            video.addEventListener('error', interrupted);
            video.addEventListener('pause', interrupted);
            this.document.body.appendChild(video);
            await video.play();
            if (!current() || this.video !== video) return;
            // Playback alone cannot prove that the OS will keep the screen on.
            this.report('video');
        } catch {
            if (!current()) return;
            this.stopResources();
            this.report('retry');
        }
    }
}

window.DashboardScreenAwake = DashboardScreenAwake;
window.addEventListener('DOMContentLoaded', () => {
    if (document.documentElement.dataset.clientMode === 'overlay') return;
    const button = document.getElementById('screen-awake-button');
    const status = document.getElementById('screen-awake-status');
    const chinese = (navigator.language || '').toLowerCase().startsWith('zh');
    const messages = chinese ? {
        off: '保持屏幕常亮', pending: '正在启用…', native: '常亮已启用',
        video: '兼容尝试：可能仍会锁屏', paused: '常亮已暂停', retry: '常亮失败，点击重试'
    } : {
        off: 'Keep screen awake', pending: 'Enabling…', native: 'Screen wake lock on',
        video: 'Compatibility attempt: screen may still lock', paused: 'Keep awake paused', retry: 'Keep awake failed: tap to retry'
    };
    const controller = new DashboardScreenAwake({
        document, navigator, secure: window.isSecureContext, media: window.ztmzWakeMedia,
        notify(value) {
            button.textContent = messages[value];
            button.setAttribute('aria-pressed', String(value === 'native' || value === 'video'));
            button.title = value === 'video' || value === 'retry'
                ? (chinese ? '若仍自动锁屏，请调整设备的自动锁屏设置。' : 'If the screen still locks, adjust your device’s auto-lock setting.')
                : messages[value];
            status.textContent = `${messages[value]}. ${button.title}`;
            if (value === 'retry' && typeof showError === 'function') {
                showError(`${messages[value]}. ${button.title}`);
            }
        }
    });
    controller.report('off');
    button.addEventListener('click', event => {
        event.stopPropagation();
        if (controller.enabled && controller.status !== 'retry') controller.disable();
        else void controller.enable();
        if (typeof showDashboardControls === 'function') showDashboardControls();
    });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState !== 'visible') controller.suspend();
        else if (controller.enabled) void controller.enable();
    });
    window.addEventListener('pagehide', () => controller.suspend());
    window.addEventListener('pageshow', event => {
        if (event.persisted && controller.enabled) void controller.enable();
    });
});
