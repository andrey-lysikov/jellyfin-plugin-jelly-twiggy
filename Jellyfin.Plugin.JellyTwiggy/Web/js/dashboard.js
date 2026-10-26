// Admin dashboard: movable, resizable and hideable blocks, devices and playback statistics.
// React owns the widgets, so they are never moved: generated CSS places them by :has() selectors.
(function (JT) {
    'use strict';

    const GRID = '#dashboardPage .content-primary > .MuiGrid-container';
    const WIDGET = `:is(${GRID} > .MuiGrid-item, ${GRID} > .MuiGrid-item > .MuiStack-root) > div:not(.MuiStack-root)`;
    const BLOCKS = [
        { id: 'server', title: 'blockServer', span: 3, sel: `${WIDGET}:has(> a[href$="/dashboard/settings"]):has(button)` },
        { id: 'itemCounts', title: 'blockItemCounts', span: 3, sel: `${WIDGET}:not(:has(> a[href]))` },
        { id: 'tasks', title: 'blockTasks', span: 6, sel: `${WIDGET}:has(> a[href$="/dashboard/tasks"])` },
        { id: 'devices', title: 'blockDevices', span: 6, sel: `${GRID} > [data-jt-block="devices"]` },
        { id: 'activity', title: 'blockActivity', span: 2, sel: `${WIDGET}:has(> a[href*="useractivity=true"])` },
        { id: 'alerts', title: 'blockAlerts', span: 2, sel: `${WIDGET}:has(> a[href*="useractivity=false"])` },
        { id: 'paths', title: 'blockPaths', span: 2, sel: `${WIDGET}:has(> a[href$="/dashboard/settings"]):not(:has(button))` },
        { id: 'stats', title: 'blockStats', span: 6, sel: `${GRID} > [data-jt-block="stats"]` }
    ];
    const LEGACY_SPANS = { third: 2, half: 3, twoThirds: 4, full: 6 };
    const CACHE_KEY = 'jt-dashboard-layout';
    const DEVICES_REFRESH_MS = 5000;
    const STATS_REFRESH_MS = 60000;
    const PERIODS = [{ days: 1, label: 'period24h' }, { days: 7, label: 'period7d' }, { days: 30, label: 'period30d' }];
    const root = document.documentElement;

    let devicesTimer = null;
    let statsTimer = null;
    let statsDays = 7;
    let edit = null; // { draft, layer, frame, drag }

    const isDashboardHome = () => /^#\/dashboard\/?(\?.*)?$/.test(JT.route());
    const enabled = () => JT.config?.IsAdmin && JT.config?.EnableDashboard;
    const block = (id) => BLOCKS.find((b) => b.id === id);

    // ---- Layout ----------------------------------------------------------------------------

    function readCache() {
        try {
            return JSON.parse(localStorage.getItem(CACHE_KEY) || 'null');
        } catch {
            return null;
        }
    }

    function writeCache(layout) {
        try {
            localStorage.setItem(CACHE_KEY, JSON.stringify(layout));
        } catch {
            // Storage unavailable: the layout still applies once settings load.
        }
    }

    function savedLayout() {
        return JT.settings?.DashboardLayout || { Order: [], Hidden: [], Sizes: {} };
    }

    function copyLayout(layout) {
        return { Order: [...(layout.Order || [])], Hidden: [...(layout.Hidden || [])], Sizes: { ...(layout.Sizes || {}) } };
    }

    // Width in columns of the 6-column grid.
    function spanOf(layout, id) {
        const value = layout.Sizes?.[id];
        const span = LEGACY_SPANS[value] ?? parseInt(value, 10);
        return span >= 1 && span <= 6 ? span : block(id).span;
    }

    function effectiveOrder(layout) {
        const order = (layout.Order || []).filter((id) => block(id));
        for (const b of BLOCKS) {
            if (!order.includes(b.id)) {
                order.push(b.id);
            }
        }
        return order;
    }

    function applyLayout(layout) {
        const hidden = new Set(layout.Hidden || []);
        const rules = effectiveOrder(layout).map((id, index) => {
            const span = spanOf(layout, id);
            const sel = block(id).sel;
            const rule = `${sel}{order:${index};--jt-span-wide:${span};--jt-span-medium:${span >= 4 ? 2 : 1}}`;
            return hidden.has(id)
                ? `${rule}\nhtml:not(.jt-dash-editing) ${sel}{display:none!important}\nhtml.jt-dash-editing ${sel}{opacity:.4}`
                : rule;
        });

        let style = document.getElementById('jt-dashboard-layout');
        if (!style) {
            style = JT.el('style', { id: 'jt-dashboard-layout' });
            document.head.appendChild(style);
        }
        style.textContent = rules.join('\n');
    }

    // Applied at script load from the last known layout, so the page doesn't jump on reload.
    const cached = readCache();
    if (cached) {
        root.classList.add('jt-dashboard');
        applyLayout(cached);
    }

    function blockTitle(id) {
        const element = document.querySelector(block(id).sel);
        const text = element?.querySelector(':scope > a, .jt-dash-block-header h3')?.textContent.trim();
        return text || JT.t(block(id).title);
    }

    // ---- Blocks ----------------------------------------------------------------------------

    function blockHeader(title, href) {
        return JT.el('a', { className: 'jt-dash-block-header', href: href || null }, [
            JT.el('h3', { text: title }),
            href ? JT.icon('chevron_right') : null
        ]);
    }

    function createDevicesBlock() {
        return JT.el('div', { className: 'jt-dash-block', 'data-jt-block': 'devices' }, [
            blockHeader(JT.t('blockDevices'), '#/dashboard/devices'),
            JT.el('div', { className: 'jt-devices-list' }, JT.el('div', { className: 'jt-empty', text: JT.t('loading') }))
        ]);
    }

    async function refreshDevices() {
        const list = document.querySelector('[data-jt-block="devices"] .jt-devices-list');
        if (!list) {
            return;
        }
        try {
            const sessions = await JT.get('JellyTwiggy/Sessions?includeIdle=true');
            list.replaceChildren(...(sessions.length
                ? sessions.map(JT.sessionCard)
                : [JT.el('div', { className: 'jt-empty', text: JT.t('noDevices') })]));
        } catch (e) {
            console.error('[JellyTwiggy] devices', e);
        }
    }

    function createStatsBlock() {
        const tabs = JT.el('div', { className: 'jt-tabs' }, PERIODS.map((period) => JT.el('button', {
            type: 'button',
            className: `jt-tab${period.days === statsDays ? ' jt-tab-active' : ''}`,
            'data-days': String(period.days),
            text: JT.t(period.label),
            onClick: () => {
                statsDays = period.days;
                tabs.querySelectorAll('.jt-tab').forEach((tab) => tab.classList.toggle('jt-tab-active', tab.dataset.days === String(statsDays)));
                refreshStats();
            }
        })));

        return JT.el('div', { className: 'jt-dash-block', 'data-jt-block': 'stats' }, [
            JT.el('div', { className: 'jt-dash-block-title-row' }, [blockHeader(JT.t('blockStats')), tabs]),
            JT.el('div', { className: 'jt-stats' }, JT.el('div', { className: 'jt-empty', text: JT.t('loading') }))
        ]);
    }

    function bar(value, max) {
        return JT.el('div', { className: 'jt-bar' }, JT.el('div', { className: 'jt-bar-fill', style: `width:${max > 0 ? Math.max(2, value * 100 / max) : 0}%` }));
    }

    function methodLabel(method) {
        return { DirectPlay: JT.t('directPlay'), DirectStream: JT.t('directStream'), Transcode: JT.t('transcode') }[method] || method;
    }

    function renderStats(stats) {
        if (!stats.TotalPlays) {
            return [JT.el('div', { className: 'jt-empty', text: JT.t('noStats') })];
        }

        const tiles = JT.el('div', { className: 'jt-stat-tiles' }, [
            JT.el('div', { className: 'jt-stat-tile' }, [JT.el('div', { className: 'jt-stat-value', text: String(stats.TotalPlays) }), JT.el('div', { className: 'jt-stat-label', text: JT.t('totalPlays') })]),
            JT.el('div', { className: 'jt-stat-tile' }, [JT.el('div', { className: 'jt-stat-value', text: JT.formatDuration(stats.TotalSeconds) }), JT.el('div', { className: 'jt-stat-label', text: JT.t('totalTime') })])
        ]);

        const maxUserSeconds = Math.max(...stats.Users.map((u) => u.Seconds), 0);
        const users = JT.el('div', { className: 'jt-stat-section' }, [
            JT.el('h4', { text: JT.t('topUsers') }),
            ...stats.Users.map((user) => JT.el('div', { className: 'jt-stat-row' }, [
                JT.avatar(user.UserId, user.UserName, undefined, 'jt-avatar-small'),
                JT.el('div', { className: 'jt-stat-row-main' }, [
                    JT.el('div', { className: 'jt-stat-row-text' }, [
                        JT.el('span', { text: user.UserName }),
                        JT.el('span', { className: 'jt-muted', text: `${JT.t('plays', { count: user.Plays })} · ${JT.formatDuration(user.Seconds)}` })
                    ]),
                    bar(user.Seconds, maxUserSeconds)
                ])
            ]))
        ]);

        const titles = JT.el('div', { className: 'jt-stat-section' }, [
            JT.el('h4', { text: JT.t('topTitles') }),
            ...stats.Titles.map((title, index) => JT.el('a', { className: 'jt-stat-row jt-stat-link', href: `#/details?id=${JT.normalizeId(title.ItemId)}` }, [
                JT.el('span', { className: 'jt-stat-rank', text: String(index + 1) }),
                JT.el('div', { className: 'jt-stat-row-text' }, [
                    JT.el('span', { text: title.Name }),
                    JT.el('span', { className: 'jt-muted', text: `${JT.t('plays', { count: title.Plays })} · ${JT.t('viewers', { count: title.Users })} · ${JT.formatDuration(title.Seconds)}` })
                ])
            ]))
        ]);

        const methods = Object.entries(stats.PlayMethods || {}).sort((a, b) => b[1] - a[1]);
        const methodTotal = methods.reduce((sum, [, count]) => sum + count, 0);
        const playMethods = JT.el('div', { className: 'jt-stat-section' }, [
            JT.el('h4', { text: JT.t('playMethods') }),
            JT.el('div', { className: 'jt-stacked-bar' }, methods.map(([method, count]) => JT.el('div', {
                className: `jt-stacked-${method.toLowerCase()}`,
                style: `flex:${count}`,
                title: `${methodLabel(method)}: ${count}`
            }))),
            JT.el('div', { className: 'jt-legend' }, methods.map(([method, count]) => JT.el('span', { className: 'jt-legend-item' }, [
                JT.el('span', { className: `jt-legend-swatch jt-stacked-${method.toLowerCase()}` }),
                `${methodLabel(method)} ${Math.round(count * 100 / methodTotal)}%`
            ])))
        ]);

        const recent = JT.el('div', { className: 'jt-stat-section jt-stat-recent' }, [
            JT.el('h4', { text: JT.t('recentPlays') }),
            ...stats.Recent.map((play) => JT.el('div', { className: 'jt-stat-row' }, [
                JT.avatar(play.UserId, play.UserName, undefined, 'jt-avatar-small'),
                JT.el('div', { className: 'jt-stat-row-text' }, [
                    JT.el('span', { text: play.SeriesName ? `${play.SeriesName} — ${play.ItemName}` : play.ItemName }),
                    JT.el('span', {
                        className: 'jt-muted',
                        text: [play.UserName, JT.formatRelative(play.EndedUtc), JT.formatDuration(play.Seconds), play.Device, play.PlayMethod ? methodLabel(play.PlayMethod) : null].filter(Boolean).join(' · ')
                    })
                ]),
                play.Completed ? JT.icon('check_circle', 'jt-completed') : null
            ]))
        ]);

        return [tiles, JT.el('div', { className: 'jt-stat-columns' }, [users, titles, playMethods]), recent];
    }

    async function refreshStats() {
        const content = document.querySelector('[data-jt-block="stats"] .jt-stats');
        if (!content) {
            return;
        }
        const days = statsDays;
        try {
            const stats = await JT.get(`JellyTwiggy/Statistics?days=${days}`);
            if (days === statsDays) {
                content.replaceChildren(...renderStats(stats));
            }
        } catch (e) {
            console.error('[JellyTwiggy] statistics', e);
            content.replaceChildren(JT.el('div', { className: 'jt-empty', text: JT.t('loadFailed') }));
        }
    }

    // ---- Edit mode -------------------------------------------------------------------------

    function gridColumns(container) {
        return getComputedStyle(container).gridTemplateColumns.split(' ').filter(Boolean).length;
    }

    function overlayFor(id) {
        const existing = edit.layer.querySelector(`[data-id="${id}"]`);
        if (existing) {
            return existing;
        }

        const eye = JT.el('button', { type: 'button', className: 'jt-icon-button jt-edit-eye', title: JT.t('visible') }, JT.icon('visibility'));
        const bar = JT.el('div', { className: 'jt-edit-bar' }, [
            JT.icon('drag_indicator', 'jt-drag-handle'),
            JT.el('span', { className: 'jt-edit-title' }),
            JT.el('span', { className: 'jt-edit-size' }),
            eye
        ]);
        const handle = JT.el('div', { className: 'jt-edit-resize', title: JT.t('blockWidth') });
        const overlay = JT.el('div', { className: 'jt-edit-block', 'data-id': id }, [bar, handle]);

        eye.addEventListener('click', () => {
            const hidden = new Set(edit.draft.Hidden);
            hidden.has(id) ? hidden.delete(id) : hidden.add(id);
            edit.draft.Hidden = [...hidden];
            applyLayout(edit.draft);
        });
        bar.addEventListener('pointerdown', (e) => {
            if (e.button === 0 && !e.target.closest('button')) {
                startDrag(e, bar, { type: 'move', id });
            }
        });
        handle.addEventListener('pointerdown', (e) => {
            const container = document.querySelector(GRID);
            const columns = container ? gridColumns(container) : 1;
            if (e.button === 0 && columns > 1) {
                const gap = parseFloat(getComputedStyle(container).columnGap) || 0;
                startDrag(e, handle, {
                    type: 'resize',
                    id,
                    startX: e.clientX,
                    columns,
                    columnWidth: (container.clientWidth + gap) / columns,
                    startSpan: columns === 6 ? spanOf(edit.draft, id) : (spanOf(edit.draft, id) >= 4 ? 2 : 1)
                });
            }
        });
        edit.layer.appendChild(overlay);
        return overlay;
    }

    function startDrag(e, element, drag) {
        e.preventDefault();
        element.setPointerCapture(e.pointerId);
        edit.drag = drag;
        edit.layer.querySelector(`[data-id="${drag.id}"]`)?.classList.add('jt-edit-active');
        const move = (event) => onDrag(event);
        const end = () => {
            element.removeEventListener('pointermove', move);
            edit?.layer.querySelector('.jt-edit-active')?.classList.remove('jt-edit-active');
            if (edit) {
                edit.drag = null;
            }
        };
        element.addEventListener('pointermove', move);
        element.addEventListener('pointerup', end, { once: true });
        element.addEventListener('pointercancel', end, { once: true });
    }

    function onDrag(e) {
        const drag = edit?.drag;
        if (!drag) {
            return;
        }

        if (drag.type === 'resize') {
            const span = Math.min(drag.columns, Math.max(1, Math.round(drag.startSpan + (e.clientX - drag.startX) / drag.columnWidth)));
            const wide = drag.columns === 6 ? span : (span === 2 ? 6 : 3);
            if (spanOf(edit.draft, drag.id) !== wide) {
                edit.draft.Sizes = { ...edit.draft.Sizes, [drag.id]: String(wide) };
                applyLayout(edit.draft);
            }
            return;
        }

        // Move: drop before or after the block under the pointer.
        for (const b of BLOCKS) {
            const element = b.id !== drag.id && document.querySelector(b.sel);
            const rect = element?.getBoundingClientRect();
            if (!rect || e.clientX < rect.left || e.clientX > rect.right || e.clientY < rect.top || e.clientY > rect.bottom) {
                continue;
            }
            const after = e.clientX > rect.left + rect.width / 2;
            const order = effectiveOrder(edit.draft).filter((id) => id !== drag.id);
            order.splice(order.indexOf(b.id) + (after ? 1 : 0), 0, drag.id);
            if (order.join() !== effectiveOrder(edit.draft).join()) {
                edit.draft.Order = order;
                applyLayout(edit.draft);
            }
            return;
        }
    }

    function positionOverlays() {
        const container = document.querySelector(GRID);
        if (!container) {
            stopEdit(false);
            return;
        }

        const hidden = new Set(edit.draft.Hidden);
        const columns = gridColumns(container);
        for (const b of BLOCKS) {
            const element = document.querySelector(b.sel);
            const rect = element?.getBoundingClientRect();
            if (!rect || rect.width === 0) {
                edit.layer.querySelector(`[data-id="${b.id}"]`)?.remove();
                continue;
            }

            const overlay = overlayFor(b.id);
            Object.assign(overlay.style, { left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.width}px`, height: `${rect.height}px` });
            overlay.classList.toggle('jt-edit-hidden', hidden.has(b.id));
            overlay.querySelector('.jt-edit-title').textContent = blockTitle(b.id);
            overlay.querySelector('.jt-edit-size').textContent = columns === 6 ? `${spanOf(edit.draft, b.id)}/6` : '';
            overlay.querySelector('.jt-edit-eye .material-icons').textContent = hidden.has(b.id) ? 'visibility_off' : 'visibility';
        }
    }

    function frame() {
        if (edit) {
            positionOverlays();
            edit.frame = requestAnimationFrame(frame);
        }
    }

    function startEdit() {
        edit = { draft: copyLayout(savedLayout()), layer: JT.el('div', { className: 'jt-edit-layer' }), frame: 0, drag: null };
        document.body.appendChild(edit.layer);
        root.classList.add('jt-dash-editing');
        renderToolbar();
        frame();
    }

    function stopEdit(keep) {
        if (!edit) {
            return;
        }
        cancelAnimationFrame(edit.frame);
        edit.layer.remove();
        edit = null;
        root.classList.remove('jt-dash-editing');
        if (!keep) {
            applyLayout(savedLayout());
        }
        renderToolbar();
    }

    async function saveEdit(button) {
        const layout = { ...edit.draft, Order: effectiveOrder(edit.draft) };
        button.disabled = true;
        try {
            await JT.post('JellyTwiggy/DashboardLayout', layout);
            JT.settings = { ...JT.settings, DashboardLayout: layout };
            writeCache(layout);
            applyLayout(layout);
            stopEdit(true);
        } catch (e) {
            console.error('[JellyTwiggy] dashboard layout', e);
            JT.toast(JT.t('saveFailed'));
            button.disabled = false;
        }
    }

    function renderToolbar() {
        const toolbar = document.querySelector('.jt-dash-toolbar');
        if (!toolbar) {
            return;
        }

        if (!edit) {
            toolbar.replaceChildren(JT.el('button', { type: 'button', className: 'jt-button', onClick: startEdit }, [JT.icon('dashboard_customize'), JT.t('customizeDashboard')]));
            return;
        }

        const save = JT.el('button', { type: 'button', className: 'jt-button jt-button-primary', text: JT.t('save') });
        save.addEventListener('click', () => saveEdit(save));
        toolbar.replaceChildren(
            JT.el('span', { className: 'jt-hint jt-edit-hint', text: JT.t('dashboardLayoutHint') }),
            JT.el('button', {
                type: 'button',
                className: 'jt-button',
                text: JT.t('resetLayout'),
                onClick: () => {
                    edit.draft = { Order: [], Hidden: [], Sizes: {} };
                    applyLayout(edit.draft);
                }
            }),
            JT.el('button', { type: 'button', className: 'jt-button', text: JT.t('cancel'), onClick: () => stopEdit(false) }),
            save);
    }

    // ---- Wiring ----------------------------------------------------------------------------

    function stopTimers() {
        clearInterval(devicesTimer);
        clearInterval(statsTimer);
        devicesTimer = statsTimer = null;
    }

    function update() {
        root.classList.toggle('jt-dashboard', !!enabled());
        if (!enabled()) {
            document.getElementById('jt-dashboard-layout')?.remove();
        }
        if (!enabled() || !isDashboardHome()) {
            stopTimers();
            return;
        }

        const container = document.querySelector(GRID);
        if (!container) {
            return;
        }

        if (!container.querySelector(':scope > [data-jt-block="devices"]')) {
            container.appendChild(createDevicesBlock());
            refreshDevices();
        }
        if (!container.querySelector(':scope > [data-jt-block="stats"]')) {
            container.appendChild(createStatsBlock());
            refreshStats();
        }
        if (!container.parentElement.querySelector(':scope > .jt-dash-toolbar')) {
            container.insertAdjacentElement('beforebegin', JT.el('div', { className: 'jt-dash-toolbar' }));
            renderToolbar();
        }

        if (!edit && JSON.stringify(savedLayout()) !== JSON.stringify(readCache())) {
            writeCache(savedLayout());
            applyLayout(savedLayout());
        } else if (!document.getElementById('jt-dashboard-layout')) {
            applyLayout(savedLayout());
        }
        if (!devicesTimer) {
            devicesTimer = setInterval(refreshDevices, DEVICES_REFRESH_MS);
            statsTimer = setInterval(refreshStats, STATS_REFRESH_MS);
        }
    }

    JT.register({
        init() {
            JT.onDomChange(update);
            JT.onNavigate(() => stopEdit(false));
        }
    });
})(window.JellyTwiggy);
