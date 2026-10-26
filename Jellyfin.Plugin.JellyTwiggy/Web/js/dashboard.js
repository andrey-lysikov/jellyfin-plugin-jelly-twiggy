// Admin dashboard: reorderable / hideable blocks, devices and playback statistics.
// React owns the widgets, so they are only tagged (data-jt-block) and laid out by generated CSS.
(function (JT) {
    'use strict';

    const BLOCKS = [
        { id: 'server', title: 'blockServer' },
        { id: 'itemCounts', title: 'blockItemCounts' },
        { id: 'tasks', title: 'blockTasks' },
        { id: 'devices', title: 'blockDevices' },
        { id: 'activity', title: 'blockActivity' },
        { id: 'alerts', title: 'blockAlerts' },
        { id: 'paths', title: 'blockPaths' },
        { id: 'stats', title: 'blockStats' }
    ];
    // Block widths: columns spanned in the 6-column (wide) and 2-column (medium) grids.
    const SIZES = { third: [2, 1], half: [3, 1], twoThirds: [4, 2], full: [6, 2] };
    const SIZE_LABELS = { third: '1/3', half: '1/2', twoThirds: '2/3' };
    const DEFAULT_SIZES = { server: 'half', itemCounts: 'half', tasks: 'full', devices: 'full', activity: 'third', alerts: 'third', paths: 'third', stats: 'full' };
    const DEVICES_REFRESH_MS = 5000;
    const STATS_REFRESH_MS = 60000;
    const PERIODS = [{ days: 1, label: 'period24h' }, { days: 7, label: 'period7d' }, { days: 30, label: 'period30d' }];

    let devicesTimer = null;
    let statsTimer = null;
    let statsDays = 7;

    const isDashboardHome = () => /^#\/dashboard\/?(\?.*)?$/.test(JT.route());
    const enabled = () => JT.config?.IsAdmin && JT.config?.EnableDashboard;

    // ---- Layout ----------------------------------------------------------------------------

    function savedLayout() {
        return JT.settings?.DashboardLayout || { Order: [], Hidden: [], Sizes: {} };
    }

    function sizeOf(layout, id) {
        const size = layout.Sizes?.[id];
        return SIZES[size] ? size : DEFAULT_SIZES[id];
    }

    function effectiveOrder(layout) {
        const known = new Set(BLOCKS.map((b) => b.id));
        const order = (layout.Order || []).filter((id) => known.has(id));
        for (const block of BLOCKS) {
            if (!order.includes(block.id)) {
                order.push(block.id);
            }
        }
        return order;
    }

    function applyLayout() {
        const layout = savedLayout();
        const rules = effectiveOrder(layout).map((id, index) => {
            const [wide, medium] = SIZES[sizeOf(layout, id)];
            return `[data-jt-block="${id}"]{order:${index};--jt-span-wide:${wide};--jt-span-medium:${medium}}`;
        });
        for (const id of layout.Hidden || []) {
            rules.push(`[data-jt-block="${id}"]{display:none!important}`);
        }

        let style = document.getElementById('jt-dashboard-layout');
        if (!style) {
            style = JT.el('style', { id: 'jt-dashboard-layout' });
            document.head.appendChild(style);
        }
        style.textContent = rules.join('\n');
    }

    function identify(widget) {
        const link = widget.querySelector(':scope > a[href]');
        if (!link) {
            return widget.querySelector('[class*="MuiGrid"]') ? 'itemCounts' : null;
        }

        const href = link.getAttribute('href') || '';
        if (href.includes('useractivity=false')) {
            return 'alerts';
        }
        if (href.includes('/dashboard/activity')) {
            return 'activity';
        }
        if (href.includes('/dashboard/tasks')) {
            return 'tasks';
        }
        if (href.includes('/dashboard/devices')) {
            return 'native-devices';
        }
        if (href.includes('/dashboard/settings')) {
            // Both the server and the paths widgets link to settings; only the server one has buttons.
            return widget.querySelector('button') ? 'server' : 'paths';
        }
        return null;
    }

    function tagWidgets(container) {
        for (const item of container.children) {
            if (item.classList.contains('jt-dash-block')) {
                continue;
            }
            const stack = item.querySelector(':scope > .MuiStack-root');
            for (const widget of (stack || item).children) {
                const id = identify(widget);
                if (id && widget.dataset.jtBlock !== id) {
                    widget.dataset.jtBlock = id;
                }
            }
        }
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

    // ---- Customize dialog ------------------------------------------------------------------

    function openLayoutDialog() {
        let layout = savedLayout();
        const hidden = new Set(layout.Hidden || []);
        const dialog = JT.dialog(JT.t('dashboardLayout'));
        dialog.body.appendChild(JT.el('p', { className: 'jt-hint', text: JT.t('dashboardLayoutHint') }));
        const list = JT.el('div', { className: 'jt-layout-list' });
        dialog.body.appendChild(list);
        let dragged = null;

        function row(id) {
            const block = BLOCKS.find((b) => b.id === id);
            const visible = JT.el('input', { type: 'checkbox', checked: !hidden.has(id), className: 'jt-layout-visible' });
            const size = JT.el('select', { className: 'jt-layout-size', 'aria-label': JT.t('blockWidth') },
                Object.keys(SIZES).map((key) => JT.el('option', { value: key, selected: key === sizeOf(layout, id), text: SIZE_LABELS[key] || JT.t('sizeFull') })));
            const element = JT.el('div', { className: 'jt-layout-row', draggable: 'true', 'data-id': id }, [
                JT.icon('drag_indicator', 'jt-drag-handle'),
                JT.el('span', { className: 'jt-layout-name', text: JT.t(block.title) }),
                JT.el('label', { className: 'jt-layout-option' }, [visible, JT.t('visible')]),
                size,
                JT.el('button', { type: 'button', className: 'jt-icon-button', title: JT.t('moveUp'), onClick: () => element.previousElementSibling?.before(element) }, JT.icon('arrow_upward')),
                JT.el('button', { type: 'button', className: 'jt-icon-button', title: JT.t('moveDown'), onClick: () => element.nextElementSibling?.after(element) }, JT.icon('arrow_downward'))
            ]);
            element.addEventListener('dragstart', (e) => {
                dragged = element;
                element.classList.add('jt-dragging');
                e.dataTransfer.effectAllowed = 'move';
                e.dataTransfer.setData('text/plain', id);
            });
            element.addEventListener('dragend', () => {
                element.classList.remove('jt-dragging');
                dragged = null;
            });
            element.addEventListener('dragover', (e) => {
                if (!dragged || dragged === element) {
                    return;
                }
                e.preventDefault();
                const rect = element.getBoundingClientRect();
                if (e.clientY < rect.top + rect.height / 2) {
                    element.before(dragged);
                } else {
                    element.after(dragged);
                }
            });
            return element;
        }

        function fill(order) {
            list.replaceChildren(...order.map(row));
        }

        fill(effectiveOrder(layout));

        const reset = JT.el('button', {
            type: 'button',
            className: 'jt-button jt-button-left',
            text: JT.t('resetLayout'),
            onClick: () => {
                hidden.clear();
                layout = { Order: [], Hidden: [], Sizes: {} };
                fill(BLOCKS.map((b) => b.id));
            }
        });
        const cancel = JT.el('button', { type: 'button', className: 'jt-button', text: JT.t('cancel'), onClick: () => dialog.close() });
        const save = JT.el('button', { type: 'button', className: 'jt-button jt-button-primary', text: JT.t('save') });
        save.addEventListener('click', async () => {
            const rows = Array.from(list.children);
            const updated = {
                Order: rows.map((r) => r.dataset.id),
                Hidden: rows.filter((r) => !r.querySelector('.jt-layout-visible').checked).map((r) => r.dataset.id),
                Sizes: Object.fromEntries(rows.map((r) => [r.dataset.id, r.querySelector('.jt-layout-size').value]))
            };
            save.disabled = true;
            try {
                await JT.post('JellyTwiggy/DashboardLayout', updated);
                JT.settings = { ...JT.settings, DashboardLayout: updated };
                applyLayout();
                dialog.close();
            } catch (e) {
                console.error('[JellyTwiggy] dashboard layout', e);
                JT.toast(JT.t('saveFailed'));
                save.disabled = false;
            }
        });
        dialog.footer.append(reset, cancel, save);
    }

    // ---- Wiring ----------------------------------------------------------------------------

    function stopTimers() {
        clearInterval(devicesTimer);
        clearInterval(statsTimer);
        devicesTimer = statsTimer = null;
    }

    function update() {
        if (!enabled() || !isDashboardHome()) {
            stopTimers();
            return;
        }

        const page = JT.visiblePage('#dashboardPage') || document.getElementById('dashboardPage');
        const container = page?.querySelector('.content-primary > .MuiGrid-container');
        if (!container) {
            return;
        }

        container.classList.add('jt-dash-grid');
        tagWidgets(container);

        if (!container.querySelector(':scope > [data-jt-block="devices"]')) {
            container.appendChild(createDevicesBlock());
            refreshDevices();
        }
        if (!container.querySelector(':scope > [data-jt-block="stats"]')) {
            container.appendChild(createStatsBlock());
            refreshStats();
        }
        if (!container.parentElement.querySelector(':scope > .jt-dash-toolbar')) {
            container.insertAdjacentElement('beforebegin', JT.el('div', { className: 'jt-dash-toolbar' },
                JT.el('button', { type: 'button', className: 'jt-button', onClick: openLayoutDialog }, [JT.icon('dashboard_customize'), JT.t('customizeDashboard')])));
        }

        if (!document.getElementById('jt-dashboard-layout')) {
            applyLayout();
        }
        if (!devicesTimer) {
            devicesTimer = setInterval(refreshDevices, DEVICES_REFRESH_MS);
            statsTimer = setInterval(refreshStats, STATS_REFRESH_MS);
        }
    }

    JT.register({
        init() {
            JT.onDomChange(update);
            JT.onNavigate(() => {
                document.getElementById('jt-dashboard-layout')?.remove();
            });
        }
    });
})(window.JellyTwiggy);
