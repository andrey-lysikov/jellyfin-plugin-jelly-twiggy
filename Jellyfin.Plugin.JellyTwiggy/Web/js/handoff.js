// "Continue here" and "Play on" between the user's devices, compatible with Swiftly Player.
// The server picks sessions and sends the commands; receivers build their own playback plan.
(function (JT) {
    'use strict';

    const TICKS_PER_SECOND = 1e7;
    const TARGETS_TTL_MS = 10000;

    JT.addStrings({
        en: {
            continueHere: 'Continue in browser',
            moveTo: 'Move to another device',
            noTargets: 'No other devices with Swiftly',
            continueHereHint: 'Stop it on that device and continue in this browser from the same position',
            continuingHere: 'Continuing here from “{device}”',
            sourceGone: 'Nothing to take over: playback on that device has ended',
            noOwnSession: 'This browser can’t receive playback right now, reload the page',
            handoffFailed: 'Could not hand over playback',
            playOn: 'Play on…',
            playOnTitle: 'Play on',
            playingOn: 'Playing on “{device}”',
            deviceUnavailable: '“{device}” is unavailable',
            nowPlayingThere: 'playing now',
            settingsHandoffSection: 'Other devices',
            enableHandoff: 'Continue watching with Swiftly clients',
            enableHandoffHint: '“Continue here” in active streams and “Play on…” on item pages and in the player, between your devices with Swiftly Player.'
        },
        ru: {
            continueHere: 'Продолжить в браузере',
            moveTo: 'Перенести на другое устройство',
            noTargets: 'Нет других устройств со Swiftly',
            continueHereHint: 'Остановить на том устройстве и продолжить в этом браузере с того же места',
            continuingHere: 'Продолжаем здесь с «{device}»',
            sourceGone: 'Забирать нечего: на том устройстве воспроизведение закончилось',
            noOwnSession: 'Этот браузер сейчас не может принять воспроизведение, обновите страницу',
            handoffFailed: 'Не удалось передать воспроизведение',
            playOn: 'Играть на…',
            playOnTitle: 'Играть на',
            playingOn: 'Играет на «{device}»',
            deviceUnavailable: 'Устройство «{device}» недоступно',
            nowPlayingThere: 'сейчас играет',
            settingsHandoffSection: 'Другие устройства',
            enableHandoff: 'Продолжать просмотр с клиентами Swiftly',
            enableHandoffHint: '«Продолжить здесь» в активных потоках и «Играть на…» на странице элемента и в плеере — между вашими устройствами со Swiftly Player.'
        },
        de: {
            continueHere: 'Im Browser fortsetzen',
            moveTo: 'Auf ein anderes Gerät verschieben',
            noTargets: 'Keine anderen Geräte mit Swiftly',
            continueHereHint: 'Auf dem anderen Gerät stoppen und in diesem Browser an derselben Stelle fortsetzen',
            continuingHere: 'Hier fortgesetzt von „{device}“',
            sourceGone: 'Nichts zu übernehmen: Die Wiedergabe auf dem Gerät ist beendet',
            noOwnSession: 'Dieser Browser kann gerade keine Wiedergabe empfangen, Seite neu laden',
            handoffFailed: 'Wiedergabe konnte nicht übergeben werden',
            playOn: 'Abspielen auf…',
            playOnTitle: 'Abspielen auf',
            playingOn: 'Läuft auf „{device}“',
            deviceUnavailable: '„{device}“ ist nicht erreichbar',
            nowPlayingThere: 'spielt gerade',
            settingsHandoffSection: 'Andere Geräte',
            enableHandoff: 'Wiedergabe mit Swiftly-Clients fortsetzen',
            enableHandoffHint: '„Hier fortsetzen“ in den aktiven Streams und „Abspielen auf…“ auf Detailseiten und im Player – zwischen Ihren Geräten mit Swiftly Player.'
        }
    });

    function failureText(e, device) {
        switch (e?.status) {
            case 404: return device ? JT.t('deviceUnavailable', { device }) : JT.t('sourceGone');
            case 409: return JT.t('noOwnSession');
            default: return JT.t('handoffFailed');
        }
    }

    // ---- Continue here ---------------------------------------------------------------------

    // Called from the active streams panel with a session the server marked CanTakeOver.
    JT.takeOver = async function (session) {
        try {
            const result = await JT.post(`JellyTwiggy/Handoff/Takeover/${encodeURIComponent(session.Id)}`);
            JT.toast(JT.t('continuingHere', { device: result?.DeviceName || session.DeviceName || '' }));
        } catch (e) {
            console.error('[JellyTwiggy] takeover', e);
            JT.toast(failureText(e));
        }
    };

    // ---- Play on ---------------------------------------------------------------------------

    // Off by default; the user turns it on in the Jelly Twiggy settings.
    const enabled = () => !!JT.settings?.EnableHandoff;

    let targets = [];
    let targetsAt = 0;
    let targetsLoading = null;

    // Targets are kept as device ids: the server resolves the current session when sending.
    function loadTargets(force) {
        if (!force && Date.now() - targetsAt < TARGETS_TTL_MS) {
            return Promise.resolve(targets);
        }
        targetsLoading ??= JT.get('JellyTwiggy/Handoff/Targets')
            .then((list) => {
                targets = list || [];
                targetsAt = Date.now();
                return targets;
            })
            .catch((e) => {
                console.error('[JellyTwiggy] handoff targets', e);
                return targets;
            })
            .finally(() => {
                targetsLoading = null;
                updateButtons();
            });
        return targetsLoading;
    }

    async function send(target, body) {
        try {
            const result = await JT.post('JellyTwiggy/Handoff/Play', { DeviceId: target.DeviceId, ...body });
            JT.toast(JT.t('playingOn', { device: result?.DeviceName || target.DeviceName || '' }));
        } catch (e) {
            console.error('[JellyTwiggy] play on', e);
            JT.toast(failureText(e, target.DeviceName || target.DeviceId));
            loadTargets(true);
        }
    }

    // A fresh list every time: the chosen device may have gone since the button appeared.
    async function pickTarget(buildBody) {
        const list = await loadTargets(true);
        if (!list.length) {
            JT.toast(JT.t('handoffFailed'));
            return;
        }

        const dialog = JT.dialog(JT.t('playOnTitle'));
        for (const target of list) {
            dialog.body.appendChild(targetRow(target, () => {
                dialog.close();
                send(target, buildBody());
            }));
        }
    }

    function targetRow(target, onPick) {
        return JT.el('button', { type: 'button', className: 'jt-handoff-target', onClick: onPick }, [
            JT.icon('tv'),
            JT.el('span', { className: 'jt-handoff-target-text' }, [
                JT.el('span', { text: target.DeviceName || target.DeviceId }),
                JT.el('span', { className: 'jt-muted', text: [target.Client, target.ItemId ? JT.t('nowPlayingThere') : null].filter(Boolean).join(' · ') })
            ])
        ]);
    }

    // ---- Move A → B (active streams panel) -------------------------------------------------

    let menu = null;

    function closeMenu() {
        menu?.remove();
        menu = null;
        document.removeEventListener('click', onMenuOutside, true);
    }

    function onMenuOutside(e) {
        if (menu && !menu.contains(e.target) && !e.target.closest('.jt-session-move')) {
            closeMenu();
        }
    }

    async function move(session, target) {
        try {
            const result = await JT.post('JellyTwiggy/Handoff/Move', { SessionId: session.Id, DeviceId: target.DeviceId });
            JT.toast(JT.t('playingOn', { device: result?.DeviceName || target.DeviceName || '' }));
        } catch (e) {
            console.error('[JellyTwiggy] move', e);
            JT.toast(failureText(e, target.DeviceName || target.DeviceId));
        }
    }

    // Drop-down under the button with the user's other Swiftly devices; the source's own is left out.
    JT.openMoveMenu = async function (session, anchor) {
        if (menu) {
            closeMenu();
            return;
        }

        const rect = anchor.getBoundingClientRect();
        const opened = menu = JT.el('div', { className: 'jt-handoff-menu' }, JT.el('div', { className: 'jt-handoff-menu-empty', text: JT.t('loading') }));
        menu.style.top = `${rect.bottom + 4}px`;
        menu.style.right = `${Math.max(8, window.innerWidth - rect.right)}px`;
        document.body.appendChild(menu);
        document.addEventListener('click', onMenuOutside, true);

        const list = (await loadTargets(true)).filter((target) => target.DeviceId !== session.DeviceId);
        if (menu !== opened) {
            return;
        }
        menu.replaceChildren(...(list.length
            ? list.map((target) => targetRow(target, () => {
                closeMenu();
                move(session, target);
            }))
            : [JT.el('div', { className: 'jt-handoff-menu-empty', text: JT.t('noTargets') })]));
    };

    // Item page: the version and tracks chosen here go along; no start position resumes a started item.
    function itemBody(page, itemId) {
        const value = (selector) => page.querySelector(selector)?.value || null;
        const index = (selector) => {
            const parsed = parseInt(value(selector), 10);
            return Number.isFinite(parsed) ? parsed : null;
        };
        return {
            ItemId: itemId,
            MediaSourceId: value('.selectSource'),
            AudioStreamIndex: index('.selectAudio'),
            SubtitleStreamIndex: index('.selectSubtitles')
        };
    }

    // Player: what plays here moves there from the current position, then stops here.
    function playerBody() {
        const time = document.querySelector('video.htmlvideoplayer')?.currentTime;
        return { FromPlayer: true, StartPositionTicks: Number.isFinite(time) ? Math.floor(time * TICKS_PER_SECOND) : null };
    }

    // Only video goes to "Play on": films, episodes, series, seasons and other videos.
    const videoItems = new Map();
    function isVideo(itemId) {
        if (!videoItems.has(itemId)) {
            videoItems.set(itemId, JT.get(`Items/${itemId}?userId=${window.ApiClient.getCurrentUserId()}`)
                .then((item) => item?.MediaType === 'Video' || item?.Type === 'Series' || item?.Type === 'Season')
                .catch(() => false));
        }
        return videoItems.get(itemId);
    }

    // jellyfin-web reuses the item page between items, so the button follows the current one.
    function placeItemButton() {
        if (!enabled()) {
            return;
        }
        const itemId = JT.route().startsWith('#/details') ? JT.routeParam('id') : null;
        const page = itemId && JT.visiblePage('.itemDetailPage');
        const play = page?.querySelector('.mainDetailButtons .btnPlay');
        if (!play) {
            return;
        }

        let button = play.parentElement.querySelector('.jt-handoff-item');
        if (!button) {
            button = JT.el('button', {
                type: 'button',
                className: 'button-flat detailButton jt-handoff-button jt-handoff-item hide',
                title: JT.t('playOn'),
                'aria-label': JT.t('playOn'),
                onClick: () => pickTarget(() => itemBody(page, button.dataset.jtItem))
            }, JT.el('div', { className: 'detailButton-content' }, JT.icon('tv', 'detailButton-icon')));
            play.insertAdjacentElement('afterend', button);
            const nativeIcon = play.querySelector('.material-icons');
            if (nativeIcon) {
                button.querySelector('.material-icons').style.fontSize = getComputedStyle(nativeIcon).fontSize;
            }
        }
        if (button.dataset.jtItem === itemId) {
            return;
        }

        button.dataset.jtItem = itemId;
        delete button.dataset.jtVideo;
        updateButtons();
        isVideo(itemId).then((video) => {
            if (video && button.dataset.jtItem === itemId) {
                button.dataset.jtVideo = 'true';
                updateButtons();
            }
        });
        loadTargets();
    }

    function placePlayerButton() {
        if (!enabled()) {
            return;
        }
        const bar = document.querySelector('#videoOsdPage:not(.hide) .videoOsdBottom');
        const settings = bar?.querySelector('.btnVideoOsdSettings');
        if (!settings || bar.querySelector('.jt-handoff-player')) {
            return;
        }

        settings.insertAdjacentElement('beforebegin', JT.el('button', {
            type: 'button',
            className: 'paper-icon-button-light autoSize jt-handoff-button jt-handoff-player hide',
            title: JT.t('playOn'),
            'aria-label': JT.t('playOn'),
            onClick: () => pickTarget(playerBody)
        }, JT.icon('tv', 'xlargePaperIconButton')));
        updateButtons();
        loadTargets();
    }

    // Buttons stay hidden while there is nowhere to send to (or the item isn't a video).
    function updateButtons() {
        document.querySelectorAll('.jt-handoff-button').forEach((button) => {
            const notVideo = button.classList.contains('jt-handoff-item') && !button.dataset.jtVideo;
            button.classList.toggle('hide', !enabled() || targets.length === 0 || notVideo);
        });
    }

    JT.register({
        init() {
            JT.onDomChange(() => {
                placeItemButton();
                placePlayerButton();
                updateButtons();
            });
            // A device opened later shows up without leaving the page.
            setInterval(() => {
                if (enabled() && !document.hidden && document.querySelector(':is(.itemDetailPage, #videoOsdPage):not(.hide) .jt-handoff-button')) {
                    loadTargets();
                }
            }, TARGETS_TTL_MS);
            JT.onNavigate(closeMenu);
            JT.onUserChange(() => {
                targets = [];
                targetsAt = 0;
                updateButtons();
            });
        }
    });
})(window.JellyTwiggy);
