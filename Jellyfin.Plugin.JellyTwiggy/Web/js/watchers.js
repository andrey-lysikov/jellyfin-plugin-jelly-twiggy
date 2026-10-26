// "Watched" row on item pages: avatars of users who watched the item, with details on hover.
(function (JT) {
    'use strict';

    let tooltip = null;

    function describe(watcher, itemType) {
        if (watcher.TotalCount != null) {
            const key = itemType === 'MusicAlbum' ? 'watchedOfTracks' : 'watchedOf';
            const text = JT.t(key, { watched: watcher.WatchedCount, total: watcher.TotalCount, count: watcher.TotalCount });
            return watcher.LastWatched ? `${text}, ${JT.t('watchedLast', { date: JT.formatDate(watcher.LastWatched) })}` : text;
        }

        const parts = [];
        if (watcher.PlayCount > 0) {
            parts.push(JT.t('watchedTimes', { count: watcher.PlayCount }));
            if (watcher.LastWatched) {
                parts.push(JT.t('watchedLast', { date: JT.formatDate(watcher.LastWatched) }));
            }
        }
        const text = parts.join(', ');
        if (watcher.ProgressPercent != null) {
            const stopped = JT.t('stoppedAt', { percent: watcher.ProgressPercent });
            return text ? `${text}. ${stopped}` : stopped;
        }
        return text;
    }

    function showTooltip(target, title, text) {
        hideTooltip();
        tooltip = JT.el('div', { className: 'jt-tooltip' }, [
            JT.el('div', { className: 'jt-tooltip-title', text: title }),
            JT.el('div', { text })
        ]);
        document.body.appendChild(tooltip);
        const rect = target.getBoundingClientRect();
        const width = tooltip.offsetWidth;
        const left = Math.min(window.innerWidth - width - 8, Math.max(8, rect.left + rect.width / 2 - width / 2));
        tooltip.style.left = `${left}px`;
        tooltip.style.top = `${rect.top - tooltip.offsetHeight - 8}px`;
        if (rect.top - tooltip.offsetHeight - 8 < 0) {
            tooltip.style.top = `${rect.bottom + 8}px`;
        }
    }

    function hideTooltip() {
        tooltip?.remove();
        tooltip = null;
    }

    function renderRow(row, watchers, itemType) {
        const content = row.querySelector('.jt-watchers');
        content.replaceChildren();
        for (const watcher of watchers) {
            const text = describe(watcher, itemType);
            const avatar = JT.el('span', {
                className: 'jt-watcher',
                tabindex: '0',
                'aria-label': `${watcher.UserName}: ${text}`
            }, JT.avatar(watcher.UserId, watcher.UserName, watcher.ImageTag));
            if (watcher.ProgressPercent != null && !(watcher.PlayCount > 0)) {
                avatar.classList.add('jt-watcher-partial');
            }
            avatar.addEventListener('mouseenter', () => showTooltip(avatar, watcher.UserName, text));
            avatar.addEventListener('focus', () => showTooltip(avatar, watcher.UserName, text));
            avatar.addEventListener('mouseleave', hideTooltip);
            avatar.addEventListener('blur', hideTooltip);
            content.appendChild(avatar);
        }
        row.hidden = watchers.length === 0;
    }

    async function load(row, itemId) {
        try {
            const [watchers, item] = await Promise.all([
                JT.get(`JellyTwiggy/Items/${itemId}/Watchers`),
                JT.get(`Users/${JT.userId}/Items/${itemId}`)
            ]);
            if (row.dataset.jtItem === itemId) {
                renderRow(row, watchers || [], item?.Type);
            }
        } catch (e) {
            console.error('[JellyTwiggy] watchers', e);
            row.hidden = true;
        }
    }

    function update() {
        if (!JT.config?.EnableWatchedBy || !JT.route().startsWith('#/details')) {
            return;
        }

        const itemId = JT.routeParam('id');
        const page = JT.visiblePage('.itemDetailPage');
        const group = page?.querySelector('.itemDetailsGroup');
        if (!itemId || !group) {
            return;
        }

        // Next to the metadata group rather than inside it: jellyfin-web empties the group on render.
        let row = page.querySelector('.jt-watched-row');
        if (!row) {
            row = JT.el('div', { className: 'detailsGroupItem jt-watched-row', hidden: true }, [
                JT.el('div', { className: 'label', text: JT.t('watchedBy') }),
                JT.el('div', { className: 'content jt-watchers' })
            ]);
        }
        if (row.previousElementSibling !== group) {
            group.insertAdjacentElement('afterend', row);
        }

        if (row.dataset.jtItem !== itemId) {
            row.dataset.jtItem = itemId;
            row.hidden = true;
            load(row, itemId);
        }
    }

    JT.register({
        init() {
            JT.onDomChange(update);
            JT.onNavigate(() => {
                hideTooltip();
                // Re-read on every visit: watching something changes the data.
                document.querySelectorAll('.jt-watched-row').forEach((row) => delete row.dataset.jtItem);
            });
        }
    });
})(window.JellyTwiggy);
