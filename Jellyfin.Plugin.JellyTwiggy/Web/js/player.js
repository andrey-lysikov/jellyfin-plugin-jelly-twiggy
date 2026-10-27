// Video player enhancements: auto-skip intro/outro, tab switch behavior, hold for 2x speed, volume peek.
(function (JT) {
    'use strict';

    const TICKS_PER_SECOND = 1e7;
    const LONG_PRESS_MS = 450;
    const FAST_RATE = 2;

    const settings = () => JT.settings || {};
    const getVideo = () => document.querySelector('video.htmlvideoplayer');

    // ---- Auto-skip -------------------------------------------------------------------------

    let currentItemId = null;
    let segments = [];
    let segmentsLoad = 0; // a newer load makes the results of older ones stale
    const skipped = new Set();

    async function fetchNowPlayingItemId(previousId) {
        // The server learns about a new item slightly after the video element does, so wait for the id
        // to change; if it never does, the item is unknown (the previous item's segments must not apply).
        for (let attempt = 0; attempt < 6; attempt++) {
            try {
                const deviceId = window.ApiClient.deviceId();
                const sessions = await JT.get(`Sessions?deviceId=${encodeURIComponent(deviceId)}`);
                const session = sessions?.find((s) => s.DeviceId === deviceId && s.NowPlayingItem);
                const found = session?.NowPlayingItem?.Id;
                if (found && found !== previousId) {
                    return found;
                }
            } catch (e) {
                console.error('[JellyTwiggy] sessions', e);
            }
            await new Promise((resolve) => setTimeout(resolve, 1000));
        }
        return null;
    }

    async function loadSegments() {
        const wanted = [];
        if (settings().AutoSkipIntro) {
            wanted.push('Intro');
        }
        if (settings().AutoSkipOutro) {
            wanted.push('Outro');
        }
        const load = ++segmentsLoad;
        segments = [];
        if (wanted.length === 0) {
            return;
        }

        const itemId = await fetchNowPlayingItemId(currentItemId);
        if (!itemId || load !== segmentsLoad) {
            return;
        }
        currentItemId = itemId;
        skipped.clear();

        try {
            const query = wanted.map((type) => `includeSegmentTypes=${type}`).join('&');
            const result = await JT.get(`MediaSegments/${itemId}?${query}`);
            if (load === segmentsLoad) {
                segments = (result?.Items || []).filter((s) => wanted.includes(s.Type));
            }
        } catch (e) {
            console.error('[JellyTwiggy] media segments', e);
        }
    }

    function onTimeUpdate(e) {
        const video = e.target;
        const ticks = video.currentTime * TICKS_PER_SECOND;
        for (const segment of segments) {
            // Skip each segment once, so seeking back into it lets the user watch it.
            if (skipped.has(segment.Id) || ticks < segment.StartTicks || ticks >= segment.EndTicks - TICKS_PER_SECOND / 2) {
                continue;
            }

            skipped.add(segment.Id);
            const end = segment.EndTicks / TICKS_PER_SECOND;
            // An outro that runs to the end: jump to the end so the next episode starts.
            video.currentTime = Number.isFinite(video.duration) ? Math.min(end, video.duration - 0.1) : end;
            JT.toast(JT.t(segment.Type === 'Intro' ? 'introSkipped' : 'outroSkipped'));
        }
    }

    // ---- Tab switching ---------------------------------------------------------------------

    let pausedByUs = false;
    let pipByUs = false;

    async function onVisibilityChange() {
        const video = getVideo();
        if (!video) {
            return;
        }

        if (document.hidden) {
            if (video.paused || video.ended) {
                return;
            }

            if (settings().AutoPipOnTabSwitch && document.pictureInPictureEnabled && !document.pictureInPictureElement) {
                try {
                    await video.requestPictureInPicture();
                    pipByUs = true;
                    return;
                } catch {
                    // Browsers may refuse PiP without a user gesture; fall back to pausing.
                }
            }

            if (settings().AutoPauseOnTabSwitch) {
                video.pause();
                pausedByUs = true;
            }
            return;
        }

        if (pipByUs) {
            pipByUs = false;
            if (document.pictureInPictureElement) {
                document.exitPictureInPicture().catch(() => {});
            }
        }

        if (pausedByUs) {
            pausedByUs = false;
            if (settings().AutoResumeOnTabSwitch && video.paused) {
                video.play().catch(() => {});
            }
        }
    }

    function setupMediaSessionPip(video) {
        // Chrome can enter PiP automatically on tab switch through this Media Session action.
        try {
            navigator.mediaSession?.setActionHandler('enterpictureinpicture', settings().AutoPipOnTabSwitch
                ? () => video.requestPictureInPicture().then(() => {
                    pipByUs = true;
                }).catch(() => {})
                : null);
        } catch {
            // Not supported by this browser.
        }
    }

    // ---- Hold for 2x speed -----------------------------------------------------------------

    let press = null; // { target, init, timer, fast, previousRate }
    let replaying = false;

    function isVideoSurface(target) {
        return target instanceof Element
            && target.closest('#videoOsdPage')
            && !target.closest('.videoOsdBottom, .upNextContainer, .osdHeader, button, input, select, a, .dialogContainer, .actionSheet');
    }

    function startFast() {
        const video = getVideo();
        if (!press || !video || video.paused) {
            return;
        }
        press.fast = true;
        press.previousRate = video.playbackRate;
        video.playbackRate = FAST_RATE;
        document.body.appendChild(JT.el('div', { className: 'jt-speed-indicator' }, [JT.icon('fast_forward'), JT.t('speedUp')]));
    }

    function stopFast() {
        const video = getVideo();
        if (video && press?.fast) {
            video.playbackRate = press.previousRate || 1;
        }
        document.querySelectorAll('.jt-speed-indicator').forEach((el) => el.remove());
    }

    // jellyfin-web toggles play/pause on pointerdown, so the press is held back until we know
    // whether it is a long press; a short press is replayed to the player unchanged.
    function onPointerDown(e) {
        if (replaying || !settings().LongPressSpeedUp || e.button !== 0 || !isVideoSurface(e.target) || !getVideo()) {
            return;
        }

        e.stopPropagation();
        press = {
            target: e.target,
            init: {
                bubbles: true,
                cancelable: true,
                pointerId: e.pointerId,
                pointerType: e.pointerType,
                isPrimary: e.isPrimary,
                button: e.button,
                buttons: e.buttons,
                clientX: e.clientX,
                clientY: e.clientY
            },
            timer: setTimeout(startFast, LONG_PRESS_MS),
            fast: false
        };
    }

    function onPointerUp() {
        if (!press) {
            return;
        }

        const current = press;
        clearTimeout(current.timer);
        if (current.fast) {
            stopFast();
        } else {
            replaying = true;
            try {
                current.target.dispatchEvent(new PointerEvent('pointerdown', current.init));
            } finally {
                replaying = false;
            }
        }
        press = null;
    }

    function onPointerCancel() {
        if (press) {
            clearTimeout(press.timer);
            stopFast();
            press = null;
        }
    }

    function onContextMenu(e) {
        // A touch hold opens the context menu on mobile browsers.
        if (press && isVideoSurface(e.target)) {
            e.preventDefault();
        }
    }

    // ---- Volume peek -----------------------------------------------------------------------

    const VOLUME_PEEK_MS = 1500;
    let peekTimer = null;

    const osdBar = () => document.querySelector('#videoOsdPage:not(.hide) .videoOsdBottom');
    const isOsdHidden = (bar) => bar.classList.contains('hide') || bar.classList.contains('videoOsdBottom-hidden');

    function endPeek(bar) {
        clearTimeout(peekTimer);
        bar.classList.remove('jt-volume-peek');
    }

    // With the controls hidden, a volume change shows just the bar's own volume block for a moment.
    function onVolumeChange() {
        if (!JT.config?.TweakPlayer) {
            return;
        }
        const bar = osdBar();
        if (!bar || (!bar.classList.contains('jt-volume-peek') && !isOsdHidden(bar))) {
            return;
        }

        if (!bar.dataset.jtPeekObserved) {
            bar.dataset.jtPeekObserved = 'true';
            // The player opened its controls: show them whole again.
            new MutationObserver(() => {
                if (bar.classList.contains('jt-volume-peek') && !isOsdHidden(bar)) {
                    endPeek(bar);
                }
            }).observe(bar, { attributes: true, attributeFilter: ['class'] });
        }

        bar.classList.add('jt-volume-peek');
        clearTimeout(peekTimer);
        peekTimer = setTimeout(() => endPeek(bar), VOLUME_PEEK_MS);
    }

    // ---- Wiring ----------------------------------------------------------------------------

    function attach() {
        const video = getVideo();
        if (!video || video.dataset.jtAttached) {
            return;
        }

        video.dataset.jtAttached = 'true';
        currentItemId = null;
        video.addEventListener('loadedmetadata', loadSegments);
        video.addEventListener('timeupdate', onTimeUpdate);
        video.addEventListener('volumechange', onVolumeChange);
        video.addEventListener('playing', () => setupMediaSessionPip(video));
        if (video.readyState >= 1) {
            loadSegments();
        }
    }

    JT.register({
        init() {
            JT.onDomChange(attach);
            document.addEventListener('visibilitychange', onVisibilityChange);
            document.addEventListener('pointerdown', onPointerDown, true);
            window.addEventListener('pointerup', onPointerUp, true);
            window.addEventListener('pointercancel', onPointerCancel, true);
            document.addEventListener('contextmenu', onContextMenu, true);
        }
    });
})(window.JellyTwiggy);
