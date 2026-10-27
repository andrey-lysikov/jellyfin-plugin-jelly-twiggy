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

    function techChips(transcode) {
        if (!transcode) {
            return null;
        }

        const direct = JT.t('techDirect');
        const parts = [
            `${JT.t('techVideo')}: ${transcode.IsVideoDirect ? direct : `${(transcode.VideoCodec || '?').toUpperCase()}${transcode.Height ? ` ${transcode.Height}p` : ''}`}`,
            `${JT.t('techAudio')}: ${transcode.IsAudioDirect ? direct : (transcode.AudioCodec || '?').toUpperCase()}`
        ];
        if (transcode.Bitrate) {
            parts.push(`${(transcode.Bitrate / 1e6).toFixed(1)} Mbps`);
        }
        if (transcode.Framerate) {
            parts.push(`${Math.round(transcode.Framerate)} fps`);
        }
        if (transcode.HardwareAcceleration && transcode.HardwareAcceleration.toLowerCase() !== 'none') {
            parts.push(`${JT.t('hardware')}: ${transcode.HardwareAcceleration.toUpperCase()}`);
        }

        return JT.el('div', { className: 'jt-session-chips' }, parts.map((text) => JT.el('span', { text })));
    }

    function userChip(session) {
        return JT.el('div', { className: 'jt-session-user' }, [
            JT.avatar(session.UserId, session.UserName, session.UserImageTag, 'jt-avatar-small'),
            JT.el('span', { text: session.UserName || '' })
        ]);
    }

    // Device and client, then the address as one unbreakable piece.
    function deviceLine(session) {
        const name = [session.DeviceName, session.Client].filter(Boolean).join(' · ');
        return JT.el('div', { className: 'jt-session-device' }, [
            name ? JT.el('span', { className: 'jt-session-device-name', text: name }) : null,
            session.RemoteEndPoint ? JT.el('span', { className: 'jt-session-device-ip', text: (name ? ' · ' : '') + session.RemoteEndPoint }) : null
        ]);
    }

    // Inline SVG so the ring spins as a vector around its own centre, without pixel snapping.
    const ICON_SVG = '<svg viewBox="0 0 1000 1000" fill="none" stroke="currentColor" stroke-width="80">'
        + '<path class="jt-streams-ring" stroke-linecap="round" d="M105 500 A395 395 0 1 1 154 690"/>'
        + '<path stroke-linejoin="round" d="M396 360 L632 500 L396 640 Z"/></svg>';

    // Poster left on a blurred copy of itself, user in the top-right corner, progress and tech at the bottom.
    JT.sessionCard = function (session) {
        const nowPlaying = session.NowPlaying;
        const key = `${session.Id || [session.UserId, session.DeviceName, session.Client].join('/')}|${nowPlaying?.ItemId || 'idle'}`;
        if (!nowPlaying) {
            return JT.el('div', { className: 'jt-session jt-session-idle', 'data-jt-key': key }, [
                JT.el('div', { className: 'jt-session-poster jt-session-poster-empty' }, JT.icon('devices')),
                JT.el('div', { className: 'jt-session-info' }, JT.el('div', { className: 'jt-session-top' }, [
                    JT.el('div', { className: 'jt-session-titles' }, [
                        deviceLine(session),
                        JT.el('div', { className: 'jt-session-status', text: `${JT.t('idle')} · ${JT.t('lastSeen', { time: JT.formatRelative(session.LastActivityDate) })}` })
                    ]),
                    userChip(session)
                ]))
            ]);
        }

        const [title, subtitle] = titleLines(nowPlaying);
        const progress = nowPlaying.RunTimeTicks ? Math.min(100, (nowPlaying.PositionTicks || 0) * 100 / nowPlaying.RunTimeTicks) : 0;
        const imageUrl = JT.imageUrl(`Items/${nowPlaying.ImageItemId}/Images/Primary`, { maxHeight: 270, quality: 90 });
        const poster = JT.el('img', { className: 'jt-session-poster', alt: '', loading: 'lazy', src: imageUrl });
        const backdrop = JT.el('div', { className: 'jt-session-bg' });
        backdrop.style.backgroundImage = `url("${imageUrl}")`;
        poster.addEventListener('error', () => {
            backdrop.remove();
            poster.replaceWith(JT.el('div', { className: 'jt-session-poster jt-session-poster-empty' }, JT.icon('movie')));
        }, { once: true });

        const methodClass = nowPlaying.PlayMethod === 'Transcode' ? 'jt-chip-warn' : 'jt-chip-ok';
        return JT.el('a', { className: 'jt-session', href: `#/details?id=${nowPlaying.ItemId}`, 'data-jt-key': key, 'data-jt-item': nowPlaying.ItemId }, [
            backdrop,
            poster,
            JT.el('div', { className: 'jt-session-info' }, [
                JT.el('div', { className: 'jt-session-top' }, [
                    JT.el('div', { className: 'jt-session-titles' }, [
                        JT.el('div', { className: 'jt-session-title', text: title || '' }),
                        subtitle ? JT.el('div', { className: 'jt-session-subtitle', text: subtitle }) : null,
                        deviceLine(session)
                    ]),
                    userChip(session)
                ]),
                JT.el('div', { className: 'jt-session-bottom' }, [
                    JT.el('div', { className: 'jt-session-progress' }, JT.el('div', { className: 'jt-session-progress-bar', style: `width:${progress}%` })),
                    JT.el('div', { className: 'jt-session-status' }, [
                        JT.icon(nowPlaying.IsPaused ? 'pause' : 'play_arrow'),
                        JT.el('span', { text: `${JT.formatTicks(nowPlaying.PositionTicks)} / ${JT.formatTicks(nowPlaying.RunTimeTicks)}` }),
                        nowPlaying.PlayMethod ? JT.el('span', { className: `jt-chip ${methodClass}`, text: playMethodLabel(nowPlaying.PlayMethod) }) : null
                    ]),
                    techChips(nowPlaying.Transcode)
                ])
            ])
        ]);
    };

    // Quality and audio of what is playing, the same badges as on posters.
    async function addBadges(card) {
        const titles = card.querySelector('.jt-session-titles');
        if (!titles || !card.dataset.jtItem || !JT.fetchBadges) {
            return;
        }
        try {
            const badges = await JT.fetchBadges(card.dataset.jtItem);
            const row = badges ? [...JT.videoBadges(badges), badges.Audio ? JT.el('span', { className: 'jt-badge', text: badges.Audio }) : null].filter(Boolean) : [];
            if (row.length && !titles.querySelector('.jt-session-badges')) {
                titles.appendChild(JT.el('div', { className: 'jt-session-badges' }, row));
            }
        } catch (e) {
            console.error('[JellyTwiggy] session badges', e);
        }
    }

    // Updates cards in place by session and item: posters and backdrops stay, so nothing flashes on refresh.
    JT.renderSessions = function (container, sessions, empty) {
        if (!sessions.length) {
            container.replaceChildren(empty);
            return;
        }

        const existing = new Map(Array.from(container.children).filter((el) => el.dataset.jtKey).map((el) => [el.dataset.jtKey, el]));
        const cards = sessions.map((session) => {
            const fresh = JT.sessionCard(session);
            const old = existing.get(fresh.dataset.jtKey);
            if (!old) {
                addBadges(fresh);
                return fresh;
            }
            for (const selector of ['.jt-session-bottom', '.jt-session-device', '.jt-session-status']) {
                const current = old.querySelector(selector);
                const next = fresh.querySelector(selector);
                if (current && next && current.outerHTML !== next.outerHTML) {
                    current.replaceWith(next);
                }
            }
            return old;
        });

        cards.forEach((card, index) => {
            if (container.children[index] !== card) {
                container.insertBefore(card, container.children[index] || null);
            }
        });
        while (container.children.length > cards.length) {
            container.lastElementChild.remove();
        }
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
        }, JT.el('span', { className: 'jt-streams-icon', 'aria-hidden': 'true' }));
        button.firstChild.innerHTML = ICON_SVG;
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
            const userMenu = toolbar.querySelector('button[aria-controls="app-user-menu"]');
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
        document.querySelectorAll('.jt-streams-button').forEach((el) => {
            el.classList.toggle('jt-streams-active', playing > 0);
            el.title = playing > 0 ? `${JT.t('activeStreams')}: ${playing}` : JT.t('activeStreams');
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

        // Nothing left to show: the panel closes, as it never opens empty.
        if (sessions.length === 0) {
            closePanel();
            return;
        }
        JT.renderSessions(panel, sessions, JT.el('span'));
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

    async function togglePanel(button) {
        if (panel) {
            closePanel();
            return;
        }

        await refresh();
        if (sessions.length === 0 || panel) {
            return;
        }

        panel = JT.el('div', { className: 'jt-streams-panel' });
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
        renderPanel();
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
