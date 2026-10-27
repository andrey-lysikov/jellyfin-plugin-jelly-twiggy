// Settings sync: jellyfin-web keeps many settings only in localStorage; this keeps them on the server too.
(function (JT) {
    'use strict';

    // Browser-wide keys from jellyfin-web's appSettings and layoutManager; user keys are "<userId>-<name>".
    const DEVICE_KEYS = new Set(['layout', 'enableGamepad', 'enableSmoothScroll', 'enableSystemExternalPlayers', 'maxStaticMusicBitrate',
        'chromecastBitrate1', 'maxVideoWidth', 'limitSupportedVideoResolution', 'preferredTranscodeVideoCodec',
        'preferredTranscodeVideoAudioCodec', 'alwaysBurnInSubtitleWhenTranscoding', 'enableDts', 'enableTrueHd', 'enableHi10p',
        'disableVbrAudio', 'alwaysRemuxFlac', 'alwaysRemuxMp3', 'aspectRatio']);
    const DEVICE_PREFIXES = ['enableautobitratebitrate-', 'maxbitrate-'];
    const RELOAD_FLAG = 'jt-sync-reloaded';
    const UPLOAD_DELAY_MS = 2000;

    let uploadTimer = null;
    let applying = false;

    const enabled = () => !!JT.settings?.SyncJellyfinSettings && !!JT.userId;
    const userPrefix = () => `${JT.userId}-`;

    function isSynced(key) {
        return !!key && (key.startsWith(userPrefix()) || DEVICE_KEYS.has(key) || DEVICE_PREFIXES.some((prefix) => key.startsWith(prefix)));
    }

    // Stored as "user:<name>" / "device:<key>", so user keys don't depend on the id prefix.
    const toStored = (key) => (key.startsWith(userPrefix()) ? `user:${key.slice(userPrefix().length)}` : `device:${key}`);
    const fromStored = (name) => (name.startsWith('user:') ? userPrefix() + name.slice(5) : name.slice(7));

    function snapshot() {
        const values = {};
        for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            if (isSynced(key)) {
                values[toStored(key)] = localStorage.getItem(key);
            }
        }
        return values;
    }

    const same = (a, b) => JSON.stringify(Object.entries(a).sort()) === JSON.stringify(Object.entries(b).sort());

    function upload() {
        uploadTimer = null;
        if (enabled()) {
            JT.post('JellyTwiggy/JellyfinSettings', snapshot()).catch((e) => console.error('[JellyTwiggy] settings sync', e));
        }
    }

    function scheduleUpload() {
        clearTimeout(uploadTimer);
        uploadTimer = setTimeout(upload, UPLOAD_DELAY_MS);
    }

    // Any change jellyfin-web saves is sent to the server shortly after.
    function watchStorage() {
        for (const method of ['setItem', 'removeItem']) {
            const original = Storage.prototype[method];
            Storage.prototype[method] = function (key) {
                const result = original.apply(this, arguments);
                if (this === localStorage && !applying && enabled() && isSynced(key)) {
                    scheduleUpload();
                }
                return result;
            };
        }
    }

    async function loadSaved() {
        try {
            return await JT.get('JellyTwiggy/JellyfinSettings') || {};
        } catch (e) {
            console.error('[JellyTwiggy] settings sync', e);
            return null;
        }
    }

    // Server values win; settings read at startup (theme, language) need one reload to take effect.
    async function restore(saved) {
        const userId = JT.userId;
        if (!enabled()) {
            return;
        }

        saved = saved || await loadSaved();
        // A user switch while loading: the values belong to the previous user.
        if (!saved || userId !== JT.userId) {
            return;
        }
        if (Object.keys(saved).length === 0) {
            upload();
            return;
        }

        let changed = false;
        applying = true;
        try {
            for (const [name, value] of Object.entries(saved)) {
                // Only names this module writes, and only keys it would sync itself.
                const key = /^(user|device):/.test(name) && typeof value === 'string' ? fromStored(name) : null;
                if (!isSynced(key)) {
                    continue;
                }
                if (localStorage.getItem(key) !== value) {
                    localStorage.setItem(key, value);
                    changed = true;
                }
            }
        } finally {
            applying = false;
        }

        if (changed && !sessionStorage.getItem(RELOAD_FLAG)) {
            sessionStorage.setItem(RELOAD_FLAG, 'true');
            window.location.reload();
            return;
        }
        if (!same(snapshot(), saved)) {
            scheduleUpload();
        }
    }

    async function turnOff() {
        const updated = { ...JT.settings, SyncJellyfinSettings: false };
        try {
            await JT.post('JellyTwiggy/UserSettings', updated);
            JT.settings = updated;
        } catch (e) {
            console.error('[JellyTwiggy] settings sync', e);
        }
    }

    // Turning the option on with a copy already on the server: the user picks which side wins.
    JT.syncSettingsNow = async function () {
        const saved = await loadSaved();
        if (!saved || Object.keys(saved).length === 0 || same(snapshot(), saved)) {
            restore(saved);
            return;
        }

        let decided = false;
        const dialog = JT.dialog(JT.t('syncAskTitle'), () => {
            if (!decided) {
                turnOff();
            }
        });
        dialog.body.appendChild(JT.el('p', { text: JT.t('syncAskText') }));
        const choose = (action) => () => {
            decided = true;
            dialog.close();
            action();
        };
        dialog.footer.append(
            JT.el('button', { type: 'button', className: 'jt-button jt-button-left', text: JT.t('cancel'), onClick: () => dialog.close() }),
            JT.el('button', { type: 'button', className: 'jt-button', text: JT.t('syncKeepLocal'), onClick: choose(upload) }),
            JT.el('button', {
                type: 'button',
                className: 'jt-button jt-button-primary',
                text: JT.t('syncApplyServer'),
                onClick: choose(() => {
                    sessionStorage.removeItem(RELOAD_FLAG);
                    restore(saved);
                })
            }));
    };

    JT.register({
        init() {
            watchStorage();
            restore();
            JT.onUserChange(() => {
                clearTimeout(uploadTimer);
                uploadTimer = null;
                restore();
            });
        }
    });
})(window.JellyTwiggy);
