// Poster badges: quality and first audio track top left, rating bottom right.
(function (JT) {
    'use strict';

    const TYPES = new Set(['Movie', 'Episode', 'Series', 'Season', 'Video', 'MusicVideo', 'Trailer', 'Audio', 'MusicAlbum']);
    const BATCH_SIZE = 100;
    const cache = new Map(); // normalized id -> badges | null
    const queue = new Set();
    let flushTimer = null;

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

    // Resolution, codec and HDR / DV badges; shared with the item page.
    JT.videoBadges = function (badges) {
        return [
            badges.Resolution ? JT.el('span', { className: `jt-badge jt-q-${slug(badges.Resolution)}`, text: badges.Resolution }) : null,
            badges.Codec ? JT.el('span', { className: `jt-badge jt-c-${slug(badges.Codec)}`, text: badges.Codec }) : null,
            badges.Range ? JT.el('span', { className: `jt-badge jt-r-${slug(badges.Range)}`, text: badges.Range }) : null
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

    function render(card, badges) {
        const host = card.querySelector('.cardScalable') || card.querySelector('.cardImageContainer');
        if (!host) {
            return;
        }

        host.querySelectorAll('.jt-badges, .jt-rating').forEach((el) => el.remove());
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

        if (badges.Rating) {
            host.appendChild(JT.el('div', { className: 'jt-rating' }, [
                JT.icon('star', 'jt-rating-star'),
                JT.el('span', { text: badges.Rating.toFixed(1) })
            ]));
        }
    }

    async function flush() {
        flushTimer = null;
        const ids = Array.from(queue);
        queue.clear();

        for (let i = 0; i < ids.length; i += BATCH_SIZE) {
            const batch = ids.slice(i, i + BATCH_SIZE);
            try {
                const result = await JT.post('JellyTwiggy/Badges', { ItemIds: batch });
                const byId = new Map(Object.entries(result || {}).map(([id, badges]) => [JT.normalizeId(id), badges]));
                for (const id of batch) {
                    cache.set(id, byId.get(id) || null);
                }
            } catch (e) {
                console.error('[JellyTwiggy] badges', e);
                continue;
            }

            for (const id of batch) {
                document.querySelectorAll(`.card[data-jt-badges="${id}"]`).forEach((card) => render(card, cache.get(id)));
            }
        }
    }

    function scan() {
        if (!JT.config?.EnablePosterBadges) {
            return;
        }

        document.querySelectorAll('.card[data-id]').forEach((card) => {
            const id = JT.normalizeId(card.dataset.id);
            // Cards are recycled by virtual lists, so compare against the id rendered last.
            if (card.dataset.jtBadges === id || !TYPES.has(card.dataset.type)) {
                return;
            }

            card.dataset.jtBadges = id;
            if (cache.has(id)) {
                render(card, cache.get(id));
            } else {
                render(card, null);
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
        }
    });
})(window.JellyTwiggy);
