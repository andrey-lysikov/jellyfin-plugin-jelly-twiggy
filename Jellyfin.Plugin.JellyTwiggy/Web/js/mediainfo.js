// Item page tweaks: "age6+" as "6+", video track as quality badges.
(function (JT) {
    'use strict';

    // The badges take the select's place in the row; the transparent select lies over them, still clickable.
    async function showVideoBadges(container, itemId) {
        container.dataset.jtVideoBadges = itemId;
        container.querySelector('.jt-video-badges')?.remove();
        container.classList.remove('jt-has-video-badges');
        try {
            const badges = await JT.fetchBadges(itemId);
            const row = badges ? JT.videoBadges(badges) : [];
            if (row.length && container.dataset.jtVideoBadges === itemId) {
                container.appendChild(JT.el('div', { className: 'jt-video-badges' }, row));
                container.classList.add('jt-has-video-badges');
            }
        } catch (e) {
            console.error('[JellyTwiggy] video badges', e);
        }
    }

    function update() {
        // Runs on every DOM change: leave at once off item pages.
        if (!JT.config?.TweakItemPage || !JT.route().startsWith('#/details')) {
            return;
        }

        const itemId = JT.route().startsWith('#/details') ? JT.routeParam('id') : null;
        const container = itemId && JT.visiblePage('.itemDetailPage')?.querySelector('.selectVideoContainer');
        if (container && JT.config?.EnablePosterBadges && container.dataset.jtVideoBadges !== itemId) {
            showVideoBadges(container, itemId);
        }

        document.querySelectorAll('.mediaInfoOfficialRating').forEach((element) => {
            // Edit text nodes in place: React keeps references to them.
            for (const node of element.childNodes) {
                if (node.nodeType === Node.TEXT_NODE && /^\s*age\s*/i.test(node.nodeValue)) {
                    node.nodeValue = node.nodeValue.replace(/^\s*age\s*/i, '');
                }
            }
        });
    }

    JT.register({
        init() {
            JT.onDomChange(update);
        }
    });
})(window.JellyTwiggy);
