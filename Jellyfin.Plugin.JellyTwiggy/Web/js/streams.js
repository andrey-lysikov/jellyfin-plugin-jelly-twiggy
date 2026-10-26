// Active streams: a header button with a live list of playback sessions.
// Also provides JT.sessionCard(), shared with the dashboard devices block.
(function (JT) {
    'use strict';

    const BADGE_REFRESH_MS = 15000;
    const PANEL_REFRESH_MS = 3000;

    let sessions = [];
    let panel = null;
    let panelTimer = null;

    // ---- Session card ----------------------------------------------------------------------

    function playMethodLabel(method) {
        switch (method) {
            case 'DirectPlay': return JT.t('directPlay');
            case 'DirectStream': return JT.t('directStream');
            case 'Transcode': return JT.t('transcode');
            default: return method || '';
        }
    }

    function titleLines(nowPlaying) {
        if (nowPlaying.SeriesName) {
            const number = nowPlaying.SeasonNumber != null && nowPlaying.EpisodeNumber != null
                ? `S${String(nowPlaying.SeasonNumber).padStart(2, '0')}E${String(nowPlaying.EpisodeNumber).padStart(2, '0')} · `
                : '';
            return [nowPlaying.SeriesName, number + (nowPlaying.Name || '')];
        }
        if (nowPlaying.Album || nowPlaying.Artists) {
            return [nowPlaying.Name, [nowPlaying.Artists, nowPlaying.Album].filter(Boolean).join(' · ')];
        }
        return [nowPlaying.Name, nowPlaying.Year ? String(nowPlaying.Year) : ''];
    }

    function transcodeDetails(transcode) {
        if (!transcode) {
            return null;
        }

        const parts = [];
        parts.push(transcode.IsVideoDirect ? 'video: direct' : `video: ${(transcode.VideoCodec || '?').toUpperCase()}${transcode.Height ? ` ${transcode.Height}p` : ''}`);
        parts.push(transcode.IsAudioDirect ? 'audio: direct' : `audio: ${(transcode.AudioCodec || '?').toUpperCase()}`);
        if (transcode.Bitrate) {
            parts.push(`${(transcode.Bitrate / 1e6).toFixed(1)} Mbps`);
        }
        if (transcode.Framerate) {
            parts.push(`${Math.round(transcode.Framerate)} fps`);
        }
        if (transcode.HardwareAcceleration && transcode.HardwareAcceleration !== 'none') {
            parts.push(`${JT.t('hardware')}: ${transcode.HardwareAcceleration}`);
        }

        return JT.el('div', { className: 'jt-session-transcode' }, [
            JT.el('div', { text: parts.join(' · ') }),
            transcode.Reasons?.length ? JT.el('div', { className: 'jt-session-reasons', text: transcode.Reasons.join(', ') }) : null
        ]);
    }

    JT.sessionCard = function (session) {
        const nowPlaying = session.NowPlaying;
        const user = JT.el('div', { className: 'jt-session-user' }, [
            JT.avatar(session.UserId, session.UserName, session.UserImageTag, 'jt-avatar-small'),
            JT.el('span', { text: session.UserName || '' })
        ]);
        const device = JT.el('div', {
            className: 'jt-session-device',
            text: [session.DeviceName, session.Client, session.RemoteEndPoint].filter(Boolean).join(' · ')
        });

        if (!nowPlaying) {
            return JT.el('div', { className: 'jt-session jt-session-idle' }, [
                JT.el('div', { className: 'jt-session-poster jt-session-poster-empty' }, JT.icon('devices')),
                JT.el('div', { className: 'jt-session-info' }, [
                    user,
                    device,
                    JT.el('div', { className: 'jt-session-status', text: `${JT.t('idle')} · ${JT.t('lastSeen', { time: JT.formatRelative(session.LastActivityDate) })}` })
                ])
            ]);
        }

        const [title, subtitle] = titleLines(nowPlaying);
        const progress = nowPlaying.RunTimeTicks ? Math.min(100, (nowPlaying.PositionTicks || 0) * 100 / nowPlaying.RunTimeTicks) : 0;
        const poster = JT.el('img', {
            className: 'jt-session-poster',
            alt: '',
            loading: 'lazy',
            src: JT.imageUrl(`Items/${nowPlaying.ImageItemId}/Images/Primary`, { maxHeight: 180, quality: 90 })
        });
        poster.addEventListener('error', () => poster.replaceWith(JT.el('div', { className: 'jt-session-poster jt-session-poster-empty' }, JT.icon('movie'))), { once: true });

        const methodClass = nowPlaying.PlayMethod === 'Transcode' ? 'jt-chip-warn' : 'jt-chip-ok';
        return JT.el('a', { className: 'jt-session', href: `#/details?id=${nowPlaying.ItemId}` }, [
            poster,
            JT.el('div', { className: 'jt-session-info' }, [
                JT.el('div', { className: 'jt-session-title', text: title || '' }),
                subtitle ? JT.el('div', { className: 'jt-session-subtitle', text: subtitle }) : null,
                user,
                device,
                JT.el('div', { className: 'jt-session-progress' }, JT.el('div', { className: 'jt-session-progress-bar', style: `width:${progress}%` })),
                JT.el('div', { className: 'jt-session-status' }, [
                    JT.icon(nowPlaying.IsPaused ? 'pause' : 'play_arrow'),
                    JT.el('span', { text: `${JT.formatTicks(nowPlaying.PositionTicks)} / ${JT.formatTicks(nowPlaying.RunTimeTicks)}` }),
                    nowPlaying.PlayMethod ? JT.el('span', { className: `jt-chip ${methodClass}`, text: playMethodLabel(nowPlaying.PlayMethod) }) : null
                ]),
                transcodeDetails(nowPlaying.Transcode)
            ])
        ]);
    };

    // ---- Header button ---------------------------------------------------------------------

    function createButton() {
        const button = JT.el('button', {
            type: 'button',
            className: 'jt-streams-button headerButton headerButtonRight paper-icon-button-light',
            title: JT.t('activeStreams'),
            'aria-label': JT.t('activeStreams'),
            onClick: (e) => {
                e.stopPropagation();
                togglePanel(button);
            }
        }, [JT.icon('live_tv'), JT.el('span', { className: 'jt-streams-count', hidden: true })]);
        return button;
    }

    function placeButton() {
        const enabled = !!JT.config?.EnableActiveStreams;
        const existing = document.querySelectorAll('.jt-streams-button');
        if (!enabled) {
            existing.forEach((el) => el.remove());
            return;
        }

        // Legacy layout header.
        const headerRight = document.querySelector('.skinHeader .headerRight');
        if (headerRight && !headerRight.querySelector('.jt-streams-button')) {
            const userButton = headerRight.querySelector('.headerUserButton');
            headerRight.insertBefore(createButton(), userButton);
            updateCount();
        }

        // Dashboard and experimental layouts (MUI toolbar): before the user menu.
        document.querySelectorAll('.MuiToolbar-root').forEach((toolbar) => {
            const userMenu = toolbar.querySelector('button[aria-haspopup="true"][aria-controls]');
            const holder = userMenu?.parentElement;
            if (holder && holder.parentElement === toolbar && !toolbar.querySelector('.jt-streams-button')) {
                const button = createButton();
                button.classList.add('jt-streams-button-mui');
                toolbar.insertBefore(button, holder);
                updateCount();
            }
        });
    }

    function updateCount() {
        const playing = sessions.filter((s) => s.NowPlaying).length;
        document.querySelectorAll('.jt-streams-count').forEach((el) => {
            el.textContent = String(playing);
            el.hidden = playing === 0;
        });
    }

    async function refresh() {
        try {
            sessions = await JT.get('JellyTwiggy/Sessions');
        } catch (e) {
            console.error('[JellyTwiggy] sessions', e);
            return;
        }
        updateCount();
        renderPanel();
    }

    // ---- Panel -----------------------------------------------------------------------------

    function renderPanel() {
        if (!panel) {
            return;
        }

        const list = panel.querySelector('.jt-streams-list');
        list.replaceChildren(...(sessions.length
            ? sessions.map(JT.sessionCard)
            : [JT.el('div', { className: 'jt-empty', text: JT.t('noActiveStreams') })]));
    }

    function closePanel() {
        panel?.remove();
        panel = null;
        clearInterval(panelTimer);
        document.removeEventListener('click', onOutsideClick, true);
    }

    function onOutsideClick(e) {
        if (panel && !panel.contains(e.target) && !e.target.closest('.jt-streams-button')) {
            closePanel();
        }
    }

    function togglePanel(button) {
        if (panel) {
            closePanel();
            return;
        }

        panel = JT.el('div', { className: 'jt-streams-panel' }, [
            JT.el('div', { className: 'jt-streams-header', text: JT.t('activeStreams') }),
            JT.el('div', { className: 'jt-streams-list' }, JT.el('div', { className: 'jt-empty', text: JT.t('loading') }))
        ]);
        const rect = button.getBoundingClientRect();
        panel.style.top = `${rect.bottom + 6}px`;
        panel.style.right = `${Math.max(8, window.innerWidth - rect.right)}px`;
        panel.addEventListener('click', (e) => {
            if (e.target.closest('a.jt-session')) {
                closePanel();
            }
        });
        document.body.appendChild(panel);
        document.addEventListener('click', onOutsideClick, true);
        refresh();
        panelTimer = setInterval(refresh, PANEL_REFRESH_MS);
    }

    JT.register({
        init() {
            JT.onDomChange(placeButton);
            JT.onNavigate(closePanel);
            refresh();
            setInterval(() => {
                if (!document.hidden && !panel && JT.config?.EnableActiveStreams) {
                    refresh();
                }
            }, BADGE_REFRESH_MS);
        }
    });
})(window.JellyTwiggy);
