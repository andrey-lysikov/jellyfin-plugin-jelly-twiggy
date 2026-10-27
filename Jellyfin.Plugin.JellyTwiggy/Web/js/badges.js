// Poster badges: quality and audio top left, ratings bottom right, played / favorite toggles and menu as badges.
(function (JT) {
    'use strict';

    const TYPES = new Set(['Movie', 'Episode', 'Series', 'Season', 'Video', 'MusicVideo', 'Trailer', 'Audio', 'MusicAlbum']);
    const BATCH_SIZE = 100;
    const cache = new Map(); // normalized id -> badges | null; the same for every user
    const userData = new Map(); // normalized id -> { played, favorite, unplayed } of the current user
    let popId = null; // item whose watched circle just changed
    const queue = new Set();
    let flushTimer = null;
    let refreshTimer = null;

    const slug = (text) => text.toLowerCase().replace(/[^a-z0-9]/g, '');

    function audioClass(audio) {
        if (/^DTS/.test(audio)) {
            return 'jt-a-dts';
        }
        if (/TrueHD|Atmos/.test(audio)) {
            return 'jt-a-atmos';
        }
        if (/^(EAC3|AC3)\b/.test(audio)) {
            return audio.startsWith('E') ? 'jt-a-eac3' : 'jt-a-ac3';
        }
        return /^(FLAC|ALAC|PCM)\b/.test(audio) ? 'jt-a-lossless' : '';
    }

    // Resolution, HDR / DV and codec badges; shared with the item page.
    JT.videoBadges = function (badges) {
        return [
            badges.Resolution ? JT.el('span', { className: `jt-badge jt-q-${slug(badges.Resolution)}`, text: badges.Resolution }) : null,
            badges.Range ? JT.el('span', { className: `jt-badge jt-r-${slug(badges.Range)}`, text: badges.Range }) : null,
            badges.Codec ? JT.el('span', { className: `jt-badge jt-c-${slug(badges.Codec)}`, text: badges.Codec }) : null
        ].filter(Boolean);
    };

    JT.fetchBadges = async function (itemId) {
        const id = JT.normalizeId(itemId);
        if (!cache.has(id)) {
            const result = await JT.post('JellyTwiggy/Badges', { ItemIds: [id] });
            cache.set(id, Object.values(result || {})[0] || null);
        }
        return cache.get(id);
    };

    // Rotten Tomatoes: a fresh tomato from 60%, a green splat below.
    const FRESH = '<svg viewBox="0 0 24 24" width="1.1em" height="1.1em"><circle cx="12" cy="13.5" r="8.5" fill="#fa320a"/><path fill="#00912d" d="M12 6.5 9 3.5l1.6 3.4L7 6l3 2.2-3.2 1.6 4.2-.5L12 12l1-2.7 4.2.5-3.2-1.6L17 6l-3.6.9L15 3.5z"/></svg>';
    const ROTTEN = '<svg viewBox="0 0 24 24" width="1.1em" height="1.1em"><path fill="#0ac855" d="M12 3c1.2 2.3 3.6 1.2 4.6 3.1 1 2-1.4 3.2.3 4.8 1.8 1.6 4.3.7 4.1 3.4-.2 2.6-3.2 1.7-3.9 3.8-.7 2.2 1.4 3.9-.9 4.6-2.3.8-2.4-2.3-4.4-2.3s-2.4 3.1-4.6 2.2c-2.2-.9.1-2.5-.7-4.5-.8-2.1-3.9-1.3-4-3.9-.1-2.7 2.6-1.7 4.3-3.4 1.6-1.6-.6-3 .5-4.9 1.1-1.9 3.6-.7 4.6-3z"/></svg>';

    function ratingBadges(badges) {
        const items = [];
        if (badges.CriticRating) {
            const critic = JT.el('div', { className: 'jt-rating' }, JT.el('span', { text: `${badges.CriticRating}%` }));
            critic.insertAdjacentHTML('afterbegin', badges.CriticRating >= 60 ? FRESH : ROTTEN);
            items.push(critic);
        }
        if (badges.Rating) {
            items.push(JT.el('div', { className: 'jt-rating' }, [
                JT.icon('star', 'jt-rating-star'),
                JT.el('span', { text: badges.Rating.toFixed(1) })
            ]));
        }
        return items.length ? JT.el('div', { className: 'jt-ratings' }, items) : null;
    }

    const CHECK = '<svg viewBox="0 0 24 24" width="1.2em" height="1.2em"><path fill="currentColor" d="M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z"/></svg>';
    const HEART = '<svg viewBox="0 0 24 24" width="1.2em" height="1.2em"><path fill="currentColor" d="m12 21.35-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54z"/></svg>';
    const MORE = '<svg viewBox="0 0 24 24" width="1.2em" height="1.2em"><path fill="currentColor" d="M12 8a2 2 0 1 0 0-4 2 2 0 0 0 0 4zm0 2a2 2 0 1 0 0 4 2 2 0 0 0 0-4zm0 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4z"/></svg>';

    // Returns the ids whose watched / favorite state differs from what the cards show.
    async function loadUserData(ids) {
        const userId = JT.userId;
        const result = await JT.get(`Items?userId=${userId}&ids=${ids.join(',')}&enableUserData=true&enableImages=false&fields=`).catch(() => null);
        const changed = [];
        if (!result || userId !== JT.userId) {
            return changed;
        }
        for (const item of result.Items || []) {
            const id = JT.normalizeId(item.Id);
            const next = { played: !!item.UserData?.Played, favorite: !!item.UserData?.IsFavorite, unplayed: item.UserData?.UnplayedItemCount || 0 };
            const previous = userData.get(id);
            if (!previous || previous.played !== next.played || previous.favorite !== next.favorite || previous.unplayed !== next.unplayed) {
                changed.push(id);
            }
            userData.set(id, next);
        }
        return changed;
    }

    const shownIds = () => Array.from(new Set(Array.from(document.querySelectorAll('.card[data-jt-badges]'), (card) => card.dataset.jtBadges)));

    // Watched state changes while browsing (playback, other devices): re-read it for the cards on screen.
    async function refreshUserData() {
        refreshTimer = null;
        const ids = shownIds();
        for (let i = 0; i < ids.length; i += BATCH_SIZE) {
            for (const id of await loadUserData(ids.slice(i, i + BATCH_SIZE))) {
                rerender(id);
            }
        }
    }

    function rerender(id) {
        document.querySelectorAll(`.card[data-jt-badges="${id}"]`).forEach((card) => render(card, cache.get(id)));
    }

    // Played and favorite toggles share the API shape: POST sets, DELETE clears.
    async function toggle(key, endpoint, id) {
        const wasOn = !!userData.get(id)?.[key];
        try {
            await JT.request(wasOn ? 'DELETE' : 'POST', `${endpoint}/${id}`);
        } catch (e) {
            console.error('[JellyTwiggy]', endpoint, e);
            return;
        }
        const current = userData.get(id) || {};
        userData.set(id, { ...current, [key]: !wasOn });
        if (key === 'played') {
            // A series' unwatched count changes with it: read the real one back.
            popId = id;
            await loadUserData([id]);
        }
        rerender(id);
        popId = null;
    }

    function toggleButton(className, svg, isOn, labels, onClick) {
        const label = JT.t(isOn ? labels[1] : labels[0]);
        const button = JT.el('button', {
            type: 'button',
            className: `jt-card-button ${className}${isOn ? ' jt-card-on' : ''}`,
            title: label,
            'aria-label': label,
            'aria-pressed': String(isOn)
        });
        button.innerHTML = svg;
        button.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            onClick();
        });
        // A card moved in the DOM would replay the pop; it plays once.
        button.addEventListener('animationend', () => button.classList.remove('jt-pop'), { once: true });
        return button;
    }

    // jellyfin-web centres its menu on the button; open it from the button's corner like a dropdown.
    // Its position is set right after the sheet is added, so a MutationObserver runs late enough to win.
    function anchorMenuTo(button) {
        const rect = button.getBoundingClientRect();
        const observer = new MutationObserver(() => {
            const sheet = Array.from(document.querySelectorAll('.actionSheet')).pop();
            if (!sheet) {
                return;
            }
            observer.disconnect();
            const width = sheet.offsetWidth;
            const height = sheet.offsetHeight;
            const below = rect.bottom + 4 + height <= window.innerHeight - 8;
            const right = rect.left + width <= window.innerWidth - 8;
            const top = below ? rect.bottom + 4 : Math.max(8, rect.top - 4 - height);
            const left = right ? rect.left : Math.max(8, rect.right - width);
            Object.assign(sheet.style, { position: 'fixed', margin: '0', left: `${left}px`, top: `${top}px` });
            sheet.style.transformOrigin = `${right ? 'left' : 'right'} ${below ? 'top' : 'bottom'}`;
        });
        observer.observe(document.body, { childList: true, subtree: true });
        setTimeout(() => observer.disconnect(), 3000);
    }

    // Replace jellyfin-web's played, favorite and "more" hover buttons, which our badges would cover.
    function cardButtons(id) {
        // Watched: green circle with a check; series with unwatched episodes: their number; else an empty ring.
        const data = userData.get(id) || {};
        const isPlayed = !!data.played;
        const left = data.unplayed || 0;
        const check = toggleButton(`jt-card-played${!isPlayed && left ? ' jt-card-count' : ''}${popId === id ? ' jt-pop' : ''}`,
            isPlayed ? CHECK : (left ? `<span>${left > 99 ? '99+' : left}</span>` : ''),
            isPlayed, ['markPlayed', 'markUnplayed'], () => toggle('played', 'UserPlayedItems', id));
        const heart = toggleButton('jt-card-fav', HEART, !!data.favorite, ['addFavorite', 'removeFavorite'], () => toggle('favorite', 'UserFavoriteItems', id));

        // jellyfin-web opens its own context menu for the card from data-action="menu".
        const more = JT.el('button', { type: 'button', className: 'jt-card-button jt-card-more itemAction', 'data-action': 'menu', title: JT.t('more'), 'aria-label': JT.t('more') });
        more.innerHTML = MORE;
        more.addEventListener('click', () => anchorMenuTo(more));
        return [check, JT.el('div', { className: 'jt-card-actions' }, [more, heart])];
    }

    function render(card, badges) {
        const host = card.querySelector('.cardScalable') || card.querySelector('.cardImageContainer');
        if (!host) {
            return;
        }

        host.querySelectorAll('.jt-badges, .jt-ratings, .jt-card-played, .jt-card-actions').forEach((el) => el.remove());
        card.classList.remove('jt-has-badges');
        if (!badges) {
            return;
        }

        const video = JT.videoBadges(badges);
        if (video.length || badges.Audio) {
            host.appendChild(JT.el('div', { className: 'jt-badges' }, [
                video.length ? JT.el('div', { className: 'jt-badge-row' }, video) : null,
                badges.Audio ? JT.el('span', { className: `jt-badge jt-badge-audio ${audioClass(badges.Audio)}`, text: badges.Audio }) : null
            ]));
        }

        const ratings = ratingBadges(badges);
        if (ratings) {
            host.appendChild(ratings);
        }

        if ((video.length || badges.Audio || ratings) && JT.config?.StyleCards) {
            card.classList.add('jt-has-badges');
            host.append(...cardButtons(card.dataset.jtBadges));
        }
    }

    async function flush() {
        flushTimer = null;
        const ids = Array.from(queue);
        queue.clear();

        for (let i = 0; i < ids.length; i += BATCH_SIZE) {
            const batch = ids.slice(i, i + BATCH_SIZE);
            const needBadges = batch.filter((id) => !cache.has(id));
            const needUserData = batch.filter((id) => !userData.has(id));
            try {
                await Promise.all([
                    needUserData.length ? loadUserData(needUserData) : null,
                    needBadges.length ? JT.post('JellyTwiggy/Badges', { ItemIds: needBadges }).then((result) => {
                        const byId = new Map(Object.entries(result || {}).map(([id, badges]) => [JT.normalizeId(id), badges]));
                        for (const id of needBadges) {
                            cache.set(id, byId.get(id) || null);
                        }
                    }) : null
                ]);
            } catch (e) {
                console.error('[JellyTwiggy] badges', e);
                continue;
            }

            for (const id of batch) {
                rerender(id);
            }
        }
    }

    // Only the cards in the changed subtrees; a full pass after navigation.
    function changedCards(roots) {
        if (!roots) {
            return document.querySelectorAll('.card[data-id]');
        }
        const cards = new Set();
        for (const root of roots) {
            if (root.nodeType !== Node.ELEMENT_NODE || !root.isConnected) {
                continue;
            }
            const own = root.closest('.card[data-id]');
            if (own) {
                cards.add(own);
            }
            root.querySelectorAll('.card[data-id]').forEach((card) => cards.add(card));
        }
        return cards;
    }

    function scan(roots) {
        if (!JT.config?.EnablePosterBadges) {
            return;
        }

        changedCards(roots).forEach((card) => {
            const id = JT.normalizeId(card.dataset.id);
            // Cards are recycled by virtual lists, so compare against the id rendered last.
            if (card.dataset.jtBadges === id || !TYPES.has(card.dataset.type) || !/^[0-9a-f]{32}$/.test(id)) {
                return;
            }

            card.dataset.jtBadges = id;
            render(card, cache.has(id) ? cache.get(id) : null);
            if (!cache.has(id) || !userData.has(id)) {
                queue.add(id);
            }
        });

        if (queue.size > 0 && !flushTimer) {
            flushTimer = setTimeout(flush, 150);
        }
    }

    JT.register({
        init() {
            JT.onDomChange(scan);
            JT.onNavigate(() => {
                clearTimeout(refreshTimer);
                refreshTimer = setTimeout(refreshUserData, 400);
            });
            // Another user's marks must not stay on the cards.
            JT.onUserChange(() => {
                userData.clear();
                shownIds().forEach((id) => {
                    rerender(id);
                    queue.add(id);
                });
                if (queue.size > 0 && !flushTimer) {
                    flushTimer = setTimeout(flush, 0);
                }
            });
        }
    });
})(window.JellyTwiggy);
