// Active streams: a header button with a live list of playback sessions.
// Also provides JT.sessionCard(), shared with the dashboard devices block.
(function (JT) {
    'use strict';

    const BADGE_REFRESH_MS = 15000;
    const PANEL_REFRESH_MS = 3000;

    let sessions = [];
    let tasks = [];
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

    // { title, note (small, after the title), subtitle }: "Series · S04E01", "Film (2004)", "Track — Artist".
    // Episode names are left out: libraries often name them after the series and number anyway.
    function titleLines(nowPlaying) {
        if (nowPlaying.SeriesName) {
            const number = nowPlaying.SeasonNumber != null && nowPlaying.EpisodeNumber != null
                ? ` · S${String(nowPlaying.SeasonNumber).padStart(2, '0')}E${String(nowPlaying.EpisodeNumber).padStart(2, '0')}`
                : '';
            return { title: nowPlaying.SeriesName + number };
        }
        if (nowPlaying.Album || nowPlaying.Artists) {
            return { title: [nowPlaying.Name, nowPlaying.Artists].filter(Boolean).join(' — '), subtitle: nowPlaying.Album || '' };
        }
        return { title: nowPlaying.Name, note: nowPlaying.Year ? `(${nowPlaying.Year})` : '' };
    }

    // "Transcode · 26.4 Mbit": play method with the bitrate sent to the client.
    function methodChip(nowPlaying) {
        if (!nowPlaying.PlayMethod) {
            return null;
        }
        const mbit = nowPlaying.Bitrate > 0 ? nowPlaying.Bitrate / 1e6 : 0;
        const rate = mbit ? JT.t('mbps', { value: mbit < 10 ? mbit.toFixed(1) : Math.round(mbit) }) : '';
        const methodClass = nowPlaying.PlayMethod === 'Transcode' ? 'jt-chip-warn' : 'jt-chip-ok';
        return JT.el('span', { className: `jt-chip ${methodClass}`, text: [playMethodLabel(nowPlaying.PlayMethod), rate].filter(Boolean).join(' · ') });
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

    // Icon-only "Continue in browser" and "Move to…" on the viewer's own playback elsewhere; handoff.js does the work.
    function takeoverButtons(session) {
        return JT.el('span', { className: 'jt-session-handoff' }, [
            JT.el('button', {
                type: 'button',
                className: 'jt-session-takeover',
                title: JT.t('continueHere'),
                'aria-label': JT.t('continueHere'),
                onClick: (e) => {
                    e.preventDefault();
                    JT.confirmTakeOver?.(session);
                }
            }, JT.icon('open_in_browser')),
            JT.el('button', {
                type: 'button',
                className: 'jt-session-takeover jt-session-move',
                title: JT.t('moveTo'),
                'aria-label': JT.t('moveTo'),
                onClick: (e) => {
                    // Keeps the panel open (it closes on clicks on a card) while the menu shows.
                    e.preventDefault();
                    e.stopPropagation();
                    JT.openMoveMenu?.(session, e.currentTarget);
                }
            }, [JT.icon('tv'), JT.icon('arrow_drop_down')])
        ]);
    }

    // Poster left on a blurred copy of itself, user in the top-right corner, progress at the bottom.
    JT.sessionCard = function (session, options) {
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

        const { title, note, subtitle } = titleLines(nowPlaying);
        const progress = nowPlaying.RunTimeTicks ? Math.min(100, (nowPlaying.PositionTicks || 0) * 100 / nowPlaying.RunTimeTicks) : 0;
        const imageUrl = JT.imageUrl(`Items/${nowPlaying.ImageItemId}/Images/Primary`, { maxHeight: 270, quality: 90 });
        const poster = JT.el('img', { className: 'jt-session-poster', alt: '', loading: 'lazy', src: imageUrl });
        const backdrop = JT.el('div', { className: 'jt-session-bg' });
        backdrop.style.backgroundImage = `url("${imageUrl}")`;
        poster.addEventListener('error', () => {
            backdrop.remove();
            poster.replaceWith(JT.el('div', { className: 'jt-session-poster jt-session-poster-empty' }, JT.icon('movie')));
        }, { once: true });

        // Poster on the full height | title, badges, device, progress.
        return JT.el('a', { className: 'jt-session', href: `#/details?id=${nowPlaying.ItemId}`, 'data-jt-key': key, 'data-jt-item': nowPlaying.ItemId }, [
            backdrop,
            poster,
            JT.el('div', { className: 'jt-session-info' }, [
                JT.el('div', { className: 'jt-session-top' }, [
                    JT.el('div', { className: 'jt-session-titles' }, [
                        JT.el('div', { className: 'jt-session-title' }, [
                            title || '',
                            note ? JT.el('span', { className: 'jt-session-note', text: note }) : null
                        ]),
                        subtitle ? JT.el('div', { className: 'jt-session-subtitle', text: subtitle }) : null
                    ]),
                    userChip(session)
                ]),
                JT.el('div', { className: 'jt-session-badges' }),
                deviceLine(session),
                // State and time left of the progress bar; play method left and handoff buttons right below.
                JT.el('div', { className: 'jt-session-bottom' }, [
                    JT.el('div', { className: 'jt-session-status' }, [
                        JT.icon(nowPlaying.IsPaused ? 'pause' : 'play_arrow'),
                        JT.el('span', { className: 'jt-session-time', text: `${JT.formatTicks(nowPlaying.PositionTicks)} / ${JT.formatTicks(nowPlaying.RunTimeTicks)}` }),
                        JT.el('div', { className: 'jt-session-progress' }, JT.el('div', { className: 'jt-session-progress-bar', style: `width:${progress}%` }))
                    ]),
                    JT.el('div', { className: 'jt-session-meta' }, [
                        methodChip(nowPlaying),
                        options?.takeover && session.CanTakeOver ? takeoverButtons(session) : null
                    ])
                ])
            ])
        ]);
    };

    // Quality and audio of what is playing (the same badges as on posters).
    async function addBadges(card) {
        const slot = card.querySelector('.jt-session-badges');
        if (!slot || !card.dataset.jtItem || !JT.fetchBadges) {
            return;
        }
        try {
            const badges = await JT.fetchBadges(card.dataset.jtItem);
            const row = [...(badges ? JT.videoBadges(badges) : []), badges?.Audio ? JT.el('span', { className: 'jt-badge', text: badges.Audio }) : null].filter(Boolean);
            if (row.length && !slot.hasChildNodes()) {
                slot.replaceChildren(...row);
            }
        } catch (e) {
            console.error('[JellyTwiggy] session badges', e);
        }
    }

    // Updates cards in place by session and item: posters and backdrops stay, so nothing flashes on refresh.
    JT.renderSessions = function (container, sessions, empty, options) {
        if (!sessions.length) {
            container.replaceChildren(empty);
            return;
        }

        const existing = new Map(Array.from(container.children).filter((el) => el.dataset.jtKey).map((el) => [el.dataset.jtKey, el]));
        const cards = sessions.map((session) => {
            const fresh = JT.sessionCard(session, options);
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

    // Playback lights the icon in the accent colour; running tasks alone only spin it.
    function updateCount() {
        const playing = sessions.filter((s) => s.NowPlaying).length;
        const title = [
            playing > 0 ? `${JT.t('activeStreams')}: ${playing}` : JT.t('activeStreams'),
            tasks.length ? `${JT.t('runningTasks')}: ${tasks.length}` : null
        ].filter(Boolean).join(' · ');
        document.querySelectorAll('.jt-streams-button').forEach((el) => {
            el.classList.toggle('jt-streams-active', playing > 0);
            el.classList.toggle('jt-streams-busy', playing === 0 && tasks.length > 0);
            el.title = title;
        });
    }

    // Running scheduled tasks; the endpoint is admin-only, so others get none.
    async function loadTasks() {
        try {
            const user = await window.ApiClient.getCurrentUser();
            if (!user?.Policy?.IsAdministrator) {
                return [];
            }
            const list = await JT.get('ScheduledTasks?isHidden=false');
            return (list || []).filter((task) => task.State === 'Running');
        } catch (e) {
            console.error('[JellyTwiggy] tasks', e);
            return [];
        }
    }

    async function refresh() {
        const [list, running] = await Promise.all([JT.get('JellyTwiggy/Sessions').catch((e) => e), loadTasks()]);
        tasks = running;
        if (list instanceof Error) {
            console.error('[JellyTwiggy] sessions', list);
        } else {
            sessions = list;
        }
        updateCount();
        renderPanel();
    }

    // ---- Panel -----------------------------------------------------------------------------

    function taskRow(task) {
        const percent = Math.round(task.CurrentProgressPercentage || 0);
        return JT.el('a', { className: 'jt-task', href: '#/dashboard/tasks', 'data-jt-key': task.Id }, [
            JT.icon('sync', 'jt-task-icon'),
            JT.el('div', { className: 'jt-task-info' }, [
                JT.el('div', { className: 'jt-task-top' }, [
                    JT.el('span', { className: 'jt-task-name', text: task.Name || '' }),
                    JT.el('span', { className: 'jt-task-percent', text: `${percent}%` })
                ]),
                JT.el('div', { className: 'jt-session-progress' }, JT.el('div', { className: 'jt-session-progress-bar', style: `width:${percent}%` }))
            ])
        ]);
    }

    // Sessions first, then running tasks (admins only) as plain tiles, without a heading;
    // "no sessions" shows only when there are no tasks either.
    function renderPanel() {
        if (!panel) {
            return;
        }

        const [list, taskList] = panel.children;
        if (!sessions.length && tasks.length) {
            list.replaceChildren();
        } else {
            JT.renderSessions(list, sessions, JT.el('div', { className: 'jt-empty', text: JT.t('noSessions') }), { takeover: true });
        }
        renderTasks(taskList);
    }

    // Rows are updated in place so the spinning icon keeps turning smoothly across refreshes.
    function renderTasks(container) {
        const existing = new Map(Array.from(container.querySelectorAll('.jt-task')).map((el) => [el.dataset.jtKey, el]));
        const rows = tasks.map((task) => {
            const fresh = taskRow(task);
            const old = existing.get(fresh.dataset.jtKey);
            if (!old) {
                return fresh;
            }
            old.querySelector('.jt-task-top').replaceWith(fresh.querySelector('.jt-task-top'));
            old.querySelector('.jt-session-progress-bar').style.width = fresh.querySelector('.jt-session-progress-bar').style.width;
            return old;
        });
        rows.forEach((el, index) => {
            if (container.children[index] !== el) {
                container.insertBefore(el, container.children[index] || null);
            }
        });
        while (container.children.length > rows.length) {
            container.lastElementChild.remove();
        }
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
        if (panel) {
            return;
        }

        panel = JT.el('div', { className: 'jt-streams-panel' }, [
            JT.el('div', { className: 'jt-streams-list' }),
            JT.el('div', { className: 'jt-streams-list' })
        ]);
        const rect = button.getBoundingClientRect();
        panel.style.top = `${rect.bottom + 6}px`;
        panel.style.right = `${Math.max(8, window.innerWidth - rect.right)}px`;
        panel.addEventListener('click', (e) => {
            if (e.target.closest('a.jt-session, a.jt-task')) {
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
