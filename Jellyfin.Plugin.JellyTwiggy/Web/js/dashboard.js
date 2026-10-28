// Admin dashboard: movable, resizable and hideable blocks, devices and playback statistics.
// React owns the widgets, so they are never moved: generated CSS places them by :has() selectors.
(function (JT) {
    'use strict';

    JT.addStrings({
        en: {
            periodAll: 'All time',
            statsRecent: 'Recent plays',
            today: 'Today',
            yesterday: 'Yesterday',
            watchTimeHint: 'Actual playing time without pauses; plays shorter than 30 seconds are not counted.',
            cpu: 'CPU',
            ram: 'RAM',
            disks: 'Disks:',
            diskRead: 'Read: {value}',
            diskWrite: 'Write: {value}'
        },
        ru: {
            periodAll: 'За всё время',
            statsRecent: 'Последние воспроизведения',
            today: 'Сегодня',
            yesterday: 'Вчера',
            watchTimeHint: 'Фактическое время воспроизведения без пауз; включения короче 30 секунд не учитываются.',
            cpu: 'ЦП',
            ram: 'ОЗУ',
            disks: 'Диски:',
            diskRead: 'Чтение: {value}',
            diskWrite: 'Запись: {value}'
        },
        de: {
            periodAll: 'Gesamt',
            statsRecent: 'Letzte Wiedergaben',
            today: 'Heute',
            yesterday: 'Gestern',
            watchTimeHint: 'Tatsächliche Wiedergabezeit ohne Pausen; Wiedergaben unter 30 Sekunden zählen nicht.',
            cpu: 'CPU',
            ram: 'RAM',
            disks: 'Festplatten:',
            diskRead: 'Lesen: {value}',
            diskWrite: 'Schreiben: {value}'
        }
    });

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
        { id: 'stats', title: 'blockStats', span: 6, sel: `${GRID} > [data-jt-block="stats"]` },
        { id: 'recent', title: 'statsRecent', span: 6, sel: `${GRID} > [data-jt-block="recent"]` }
    ];
    const LEGACY_SPANS = { third: 2, half: 3, twoThirds: 4, full: 6 };
    const CACHE_KEY = 'jt-dashboard-layout';
    const ROWS_CACHE_KEY = 'jt-dashboard-rows';
    // Masonry: blocks span rows of ROW_PX by their height, BLOCK_GAP_PX below each.
    const ROW_PX = 4;
    const BLOCK_GAP_PX = 16;
    const DEVICES_REFRESH_MS = 5000;
    const STATS_REFRESH_MS = 60000;
    const METRICS_REFRESH_MS = 3000;
    const RESOLUTION_CARDS = [['MovieIcon', 'Movie'], ['TvIcon', 'Episode'], ['MusicVideoIcon', 'MusicVideo']];
    const RESOLUTION_KEYS = ['4K', '1080p', '720p', 'SD', 'HDR', 'DV'];
    const PERIODS = [{ days: 1, label: 'period24h' }, { days: 7, label: 'period7d' }, { days: 30, label: 'period30d' }, { days: 0, label: 'periodAll' }];
    const RECENT_DAYS = 0; // the latest plays whenever they happened
    const root = document.documentElement;

    let devicesTimer = null;
    let statsTimer = null;
    let metricsTimer = null;
    let metrics = null;
    let resolutions = null;
    let resolutionsLoading = false;
    let statsDays = 7;
    let lastStats = null;
    let edit = null; // { draft, layer, frame, drag }
    let rowsFrame = 0;
    const observed = new WeakSet();
    const resizeObserver = new ResizeObserver(() => scheduleRows());

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
        try {
            const rows = localStorage.getItem(ROWS_CACHE_KEY);
            if (rows) {
                applyRows(rows);
            }
        } catch {
            // Storage unavailable.
        }
    }

    // Row spans live in their own stylesheet: React owns the elements, so no inline styles.
    function updateRows() {
        rowsFrame = 0;
        const rules = [];
        for (const b of BLOCKS) {
            const element = document.querySelector(b.sel);
            if (!element) {
                continue;
            }
            if (!observed.has(element)) {
                observed.add(element);
                resizeObserver.observe(element);
            }
            const height = element.getBoundingClientRect().height;
            if (height > 0) {
                rules.push(`${b.sel}{grid-row-end:span ${Math.ceil((height + BLOCK_GAP_PX) / ROW_PX)}}`);
            }
        }

        if (rules.length > 0) {
            applyRows(rules.join('\n'));
            try {
                localStorage.setItem(ROWS_CACHE_KEY, rules.join('\n'));
            } catch {
                // Storage unavailable: rows are recomputed on every visit anyway.
            }
        }
    }

    // Masonry rows switch on only with spans known, otherwise blocks would overlap.
    function applyRows(css) {
        let style = document.getElementById('jt-dashboard-rows');
        if (!style) {
            style = JT.el('style', { id: 'jt-dashboard-rows' });
            document.head.appendChild(style);
        }
        style.textContent = css;
        root.classList.add('jt-masonry');
    }

    function scheduleRows() {
        if (!rowsFrame) {
            rowsFrame = requestAnimationFrame(updateRows);
        }
    }

    function blockTitle(id) {
        const element = document.querySelector(block(id).sel);
        const text = element?.querySelector(':scope > a, .jt-dash-block-header h3')?.textContent.trim();
        return text || JT.t(block(id).title);
    }

    // ---- Blocks ----------------------------------------------------------------------------

    // Old dashboard pages may linger hidden in the DOM: only the visible copy of a block is updated.
    function ownBlock(id) {
        return Array.from(document.querySelectorAll(`[data-jt-block="${id}"]`)).find((el) => el.getClientRects().length > 0) || null;
    }

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
        const list = ownBlock('devices')?.querySelector('.jt-devices-list');
        if (!list) {
            return;
        }
        try {
            const sessions = await JT.get('JellyTwiggy/Sessions?includeIdle=true');
            JT.renderSessions(list, sessions, JT.el('div', { className: 'jt-empty', text: JT.t('noDevices') }));
        } catch (e) {
            console.error('[JellyTwiggy] devices', e);
        }
    }

    function tabGroup(items, isActive, onSelect) {
        const group = JT.el('div', { className: 'jt-tabs' });
        const paint = () => group.querySelectorAll('.jt-tab').forEach((tab, i) => tab.classList.toggle('jt-tab-active', isActive(items[i])));
        for (const item of items) {
            group.appendChild(JT.el('button', {
                type: 'button',
                className: 'jt-tab',
                text: JT.t(item.label),
                onClick: () => {
                    onSelect(item);
                    paint();
                }
            }));
        }
        paint();
        return group;
    }

    function createStatsBlock() {
        const periods = tabGroup(PERIODS, (p) => p.days === statsDays, (p) => {
            statsDays = p.days;
            refreshStats();
        });

        return JT.el('div', { className: 'jt-dash-block', 'data-jt-block': 'stats' }, [
            JT.el('div', { className: 'jt-dash-block-title-row' }, [
                blockHeader(JT.t('blockStats')),
                JT.el('div', { className: 'jt-stats-controls' }, periods)
            ]),
            JT.el('div', { className: 'jt-stats' }, JT.el('div', { className: 'jt-empty', text: JT.t('loading') }))
        ]);
    }

    function bar(value, max) {
        return JT.el('div', { className: 'jt-bar' }, JT.el('div', { className: 'jt-bar-fill', style: `width:${max > 0 ? Math.max(2, value * 100 / max) : 0}%` }));
    }

    function methodLabel(method) {
        return { DirectPlay: JT.t('directPlay'), DirectStream: JT.t('directStream'), Transcode: JT.t('transcode') }[method] || method;
    }

    function renderOverview(stats) {
        const tiles = JT.el('div', { className: 'jt-stat-tiles' }, [
            JT.el('div', { className: 'jt-stat-tile' }, [JT.el('div', { className: 'jt-stat-value', text: String(stats.TotalPlays) }), JT.el('div', { className: 'jt-stat-label', text: JT.t('totalPlays') })]),
            JT.el('div', { className: 'jt-stat-tile', title: JT.t('watchTimeHint') }, [JT.el('div', { className: 'jt-stat-value', text: JT.formatDuration(stats.TotalSeconds) }), JT.el('div', { className: 'jt-stat-label', text: JT.t('totalTime') })])
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

        const titles = JT.el('div', { className: 'jt-stat-section' }, [
            JT.el('h4', { text: JT.t('topTitles') }),
            ...stats.Titles.slice(0, 10).map((title, index) => JT.el('a', { className: 'jt-stat-row jt-stat-link', href: `#/details?id=${JT.normalizeId(title.ItemId)}` }, [
                JT.el('span', { className: 'jt-stat-rank', text: String(index + 1) }),
                JT.el('div', { className: 'jt-stat-row-text' }, [
                    JT.el('span', { text: title.Name }),
                    JT.el('span', { className: 'jt-muted', text: `${JT.t('plays', { count: title.Plays })} · ${JT.t('viewers', { count: title.Users })} · ${JT.formatDuration(title.Seconds)}` })
                ])
            ]))
        ]);

        return [tiles, JT.el('div', { className: 'jt-stat-overview' }, [JT.el('div', { className: 'jt-stat-side' }, [playMethods, titles]), users])];
    }

    function createRecentBlock() {
        return JT.el('div', { className: 'jt-dash-block', 'data-jt-block': 'recent' }, [
            blockHeader(JT.t('statsRecent'), '#/dashboard/activity'),
            JT.el('div', { className: 'jt-recent' }, JT.el('div', { className: 'jt-empty', text: JT.t('loading') }))
        ]);
    }

    async function refreshRecent() {
        const content = ownBlock('recent')?.querySelector('.jt-recent');
        if (!content) {
            return;
        }
        try {
            const stats = await JT.get(`JellyTwiggy/Statistics?days=${RECENT_DAYS}`);
            content.replaceChildren(...(stats.Recent.length ? renderRecent(stats) : [JT.el('div', { className: 'jt-empty', text: JT.t('noStats') })]));
        } catch (e) {
            console.error('[JellyTwiggy] recent plays', e);
            content.replaceChildren(JT.el('div', { className: 'jt-empty', text: JT.t('loadFailed') }));
        }
    }

    const toDate = (value) => new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : `${value}Z`);

    function dayLabel(date) {
        const start = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
        const days = Math.round((start(new Date()) - start(date)) / 86400000);
        if (days === 0) {
            return JT.t('today');
        }
        if (days === 1) {
            return JT.t('yesterday');
        }
        return date.toLocaleDateString(JT.language(), { day: 'numeric', month: 'long', year: date.getFullYear() === new Date().getFullYear() ? undefined : 'numeric' });
    }

    // Grouped by day; each row: who, what (series on top, episode below), then device, method, length and time.
    function renderRecent(stats) {
        const groups = [];
        for (const play of stats.Recent) {
            const ended = toDate(play.EndedUtc);
            const label = dayLabel(ended);
            if (groups.at(-1)?.label !== label) {
                groups.push({ label, plays: [] });
            }
            groups.at(-1).plays.push({ play, ended });
        }

        return groups.map((group) => JT.el('section', { className: 'jt-recent-day' }, [
            JT.el('h4', { text: group.label }),
            ...group.plays.map(({ play, ended }) => JT.el('div', { className: `jt-recent-row${play.Completed ? ' jt-recent-done' : ''}` }, [
                JT.avatar(play.UserId, play.UserName, undefined, 'jt-avatar-small'),
                JT.el('div', { className: 'jt-recent-title' }, [
                    JT.el('span', { className: 'jt-recent-main', text: play.SeriesName || play.ItemName }),
                    JT.el('span', { className: 'jt-recent-sub', text: [play.SeriesName ? play.ItemName : null, play.UserName].filter(Boolean).join(' · ') })
                ]),
                JT.el('span', { className: 'jt-recent-device', text: play.Device || play.Client || '', title: [play.Device, play.Client].filter(Boolean).join(' · ') }),
                play.PlayMethod
                    ? JT.el('span', { className: `jt-chip ${play.PlayMethod === 'Transcode' ? 'jt-chip-warn' : 'jt-chip-ok'}`, text: methodLabel(play.PlayMethod) })
                    : JT.el('span'),
                JT.el('span', { className: 'jt-recent-length', text: JT.formatDuration(play.Seconds) }),
                JT.el('span', { className: 'jt-recent-time', text: ended.toLocaleTimeString(JT.language(), { hour: '2-digit', minute: '2-digit' }) }),
                play.Completed ? JT.icon('check_circle', 'jt-completed') : JT.el('span')
            ]))
        ]));
    }

    function renderStatsContent() {
        const content = ownBlock('stats')?.querySelector('.jt-stats');
        if (!content || !lastStats) {
            return;
        }
        if (!lastStats.TotalPlays) {
            content.replaceChildren(JT.el('div', { className: 'jt-empty', text: JT.t('noStats') }));
            return;
        }
        content.replaceChildren(...renderOverview(lastStats));
    }

    async function refreshStats() {
        const content = ownBlock('stats')?.querySelector('.jt-stats');
        if (!content) {
            return;
        }
        const days = statsDays;
        try {
            const stats = await JT.get(`JellyTwiggy/Statistics?days=${days}`);
            if (days === statsDays) {
                lastStats = stats;
                renderStatsContent();
            }
        } catch (e) {
            console.error('[JellyTwiggy] statistics', e);
            content.replaceChildren(JT.el('div', { className: 'jt-empty', text: JT.t('loadFailed') }));
        }
    }

    // ---- Library resolutions and system load -----------------------------------------------

    // Badges go into React-owned MetricCards; added only when missing, so re-renders just re-add them.
    function decorateCounts() {
        const widget = resolutions && document.querySelector(block('itemCounts').sel);
        if (!widget) {
            return;
        }
        for (const [icon, kind] of RESOLUTION_CARDS) {
            const card = widget.querySelector(`svg[data-testid="${icon}"]`)?.closest('.MuiCard-root');
            const counts = resolutions[kind];
            if (!card || !counts || card.querySelector(':scope > .jt-res-row')) {
                continue;
            }
            const badges = RESOLUTION_KEYS.filter((key) => counts[key] > 0).map((key) => JT.el('span', {
                className: `jt-badge ${key === 'HDR' || key === 'DV' ? 'jt-r' : 'jt-q'}-${key.toLowerCase()}`,
                text: `${counts[key].toLocaleString(JT.language())} · ${key}`
            }));
            if (badges.length) {
                card.classList.add('jt-res-card');
                card.appendChild(JT.el('div', { className: 'jt-res-row' }, badges));
            }
        }
    }

    async function loadResolutions() {
        if (resolutions || resolutionsLoading) {
            decorateCounts();
            return;
        }
        resolutionsLoading = true;
        try {
            resolutions = await JT.get('JellyTwiggy/LibraryResolutions');
            decorateCounts();
        } catch (e) {
            console.error('[JellyTwiggy] library resolutions', e);
        } finally {
            resolutionsLoading = false;
        }
    }

    const DONUT_R = 40;
    const DONUT_C = 2 * Math.PI * DONUT_R;

    function donut(label) {
        const element = JT.el('div', { className: 'jt-donut' });
        element.innerHTML = `<svg viewBox="0 0 96 96" width="100%" height="100%"><circle cx="48" cy="48" r="${DONUT_R}" class="jt-donut-track"/>`
            + `<circle cx="48" cy="48" r="${DONUT_R}" class="jt-donut-value" stroke-dasharray="0 ${DONUT_C}" transform="rotate(-90 48 48)"/></svg>`;
        element.append(JT.el('div', { className: 'jt-donut-text' }, [
            JT.el('span', { className: 'jt-donut-percent' }),
            JT.el('span', { className: 'jt-donut-label', text: label })
        ]));
        return element;
    }

    // Values change in place, so the panel doesn't flash on every poll.
    function setDonut(element, label, percent) {
        element.hidden = percent == null;
        if (percent == null) {
            return;
        }
        const level = percent >= 85 ? 'high' : percent >= 60 ? 'mid' : 'low';
        element.className = `jt-donut jt-donut-${level}`;
        element.title = `${label}: ${Math.round(percent)}%`;
        element.querySelector('.jt-donut-value').setAttribute('stroke-dasharray', `${DONUT_C * percent / 100} ${DONUT_C}`);
        element.querySelector('.jt-donut-percent').textContent = `${Math.round(percent)}%`;
    }

    function speed(bytesPerSecond) {
        const units = ['B/s', 'KB/s', 'MB/s', 'GB/s'];
        let value = bytesPerSecond || 0;
        let unit = 0;
        while (value >= 1024 && unit < units.length - 1) {
            value /= 1024;
            unit++;
        }
        return `${value.toFixed(value < 10 && unit > 0 ? 1 : 0)} ${units[unit]}`;
    }

    function buildMetrics(panel) {
        panel.append(
            JT.el('div', { className: 'jt-donuts' }, [donut(JT.t('cpu')), donut(JT.t('ram'))]),
            JT.el('div', { className: 'jt-disks' }, [
                JT.el('span', { className: 'jt-disks-label', text: JT.t('disks') }),
                JT.el('span', { className: 'jt-disk-read' }),
                JT.el('span', { className: 'jt-disk-write' })
            ]));
    }

    function fillMetrics(panel) {
        const [cpu, ram] = panel.querySelectorAll('.jt-donut');
        setDonut(cpu, JT.t('cpu'), metrics.CpuPercent);
        setDonut(ram, JT.t('ram'), metrics.MemoryPercent);
        const disks = panel.querySelector('.jt-disks');
        disks.hidden = metrics.DiskReadBytesPerSecond == null;
        disks.querySelector('.jt-disk-read').textContent = JT.t('diskRead', { value: speed(metrics.DiskReadBytesPerSecond) });
        disks.querySelector('.jt-disk-write').textContent = JT.t('diskWrite', { value: speed(metrics.DiskWriteBytesPerSecond) });
    }

    // Lives in the server widget's info row (name/versions), right side; hidden where the server has no data.
    function metricsPanel(create) {
        const row = document.querySelector(block('server').sel)?.querySelector('.MuiPaper-root > .MuiStack-root');
        let panel = row?.querySelector(':scope > .jt-sysmetrics') || null;
        const available = metrics && (metrics.CpuPercent != null || metrics.MemoryPercent != null);
        if (!available) {
            panel?.remove();
            return null;
        }
        if (!panel && row && create) {
            panel = JT.el('div', { className: 'jt-sysmetrics' });
            buildMetrics(panel);
            fillMetrics(panel);
            row.appendChild(panel);
        }
        return panel;
    }

    async function refreshMetrics() {
        try {
            metrics = await JT.get('JellyTwiggy/SystemMetrics');
        } catch (e) {
            console.error('[JellyTwiggy] system metrics', e);
            return;
        }
        const panel = metricsPanel(true);
        if (panel) {
            fillMetrics(panel);
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

        const visible = JT.el('input', { type: 'checkbox', className: 'jt-switch', title: JT.t('visible'), 'aria-label': JT.t('visible') });
        const bar = JT.el('div', { className: 'jt-edit-bar' }, [
            JT.icon('drag_indicator', 'jt-drag-handle'),
            JT.el('span', { className: 'jt-edit-title' }),
            JT.el('span', { className: 'jt-edit-size' }),
            visible
        ]);
        const handle = JT.el('div', { className: 'jt-edit-resize', title: JT.t('blockWidth') });
        const overlay = JT.el('div', { className: 'jt-edit-block', 'data-id': id }, [bar, handle]);

        visible.addEventListener('change', () => {
            const hidden = new Set(edit.draft.Hidden);
            visible.checked ? hidden.delete(id) : hidden.add(id);
            edit.draft.Hidden = [...hidden];
            applyLayout(edit.draft);
        });
        bar.addEventListener('pointerdown', (e) => {
            if (e.button === 0 && !e.target.closest('button, input')) {
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
            overlay.querySelector('.jt-switch').checked = !hidden.has(b.id);
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

    const headerButton = (icon, title, onClick) => JT.el('button', {
        type: 'button', className: 'jt-header-icon-button', title, 'aria-label': title, onClick
    }, JT.icon(icon));

    // Edit controls live in the MUI header, left of the active streams button (or the user menu);
    // the page toolbar only carries the hint while editing.
    function renderToolbar() {
        const toolbar = document.querySelector('.jt-dash-toolbar');
        toolbar?.replaceChildren(...(edit ? [JT.el('span', { className: 'jt-hint jt-edit-hint', text: JT.t('dashboardLayoutHint') })] : []));
        document.querySelectorAll('.jt-dash-header').forEach(renderHeader);
    }

    function renderHeader(group) {
        if (!edit) {
            group.replaceChildren(headerButton('edit', JT.t('customizeDashboard'), startEdit));
            return;
        }
        const save = headerButton('check', JT.t('save'), () => saveEdit(save));
        group.replaceChildren(
            headerButton('restart_alt', JT.t('resetLayout'), () => {
                edit.draft = { Order: [], Hidden: [], Sizes: {} };
                applyLayout(edit.draft);
            }),
            headerButton('close', JT.t('cancel'), () => stopEdit(false)),
            save);
    }

    function placeHeader(show) {
        if (!show) {
            document.querySelectorAll('.jt-dash-header').forEach((el) => el.remove());
            return;
        }
        document.querySelectorAll('.MuiToolbar-root').forEach((toolbar) => {
            const holder = toolbar.querySelector('button[aria-controls="app-user-menu"]')?.parentElement;
            if (!holder || holder.parentElement !== toolbar || toolbar.querySelector(':scope > .jt-dash-header')) {
                return;
            }
            const group = JT.el('span', { className: 'jt-dash-header' });
            renderHeader(group);
            toolbar.insertBefore(group, toolbar.querySelector(':scope > .jt-streams-button') || holder);
        });
    }

    // ---- Wiring ----------------------------------------------------------------------------

    function stopTimers() {
        clearInterval(devicesTimer);
        clearInterval(statsTimer);
        clearInterval(metricsTimer);
        devicesTimer = statsTimer = metricsTimer = null;
    }

    function update() {
        root.classList.toggle('jt-dashboard', !!enabled());
        if (!enabled()) {
            document.getElementById('jt-dashboard-layout')?.remove();
            document.getElementById('jt-dashboard-rows')?.remove();
            root.classList.remove('jt-masonry');
        }
        placeHeader(enabled() && isDashboardHome());
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
        if (!container.querySelector(':scope > [data-jt-block="recent"]')) {
            container.appendChild(createRecentBlock());
            refreshRecent();
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
        loadResolutions();
        metricsPanel(true);
        scheduleRows();
        if (!devicesTimer) {
            devicesTimer = setInterval(refreshDevices, DEVICES_REFRESH_MS);
            statsTimer = setInterval(() => {
                refreshStats();
                refreshRecent();
            }, STATS_REFRESH_MS);
            metricsTimer = setInterval(refreshMetrics, METRICS_REFRESH_MS);
            refreshMetrics();
        }
    }

    JT.register({
        init() {
            JT.onDomChange(update);
            JT.onNavigate(() => stopEdit(false));
        }
    });
})(window.JellyTwiggy);
