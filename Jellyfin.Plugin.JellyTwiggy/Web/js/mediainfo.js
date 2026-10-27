// Item page tweaks: "age6+" as "6+", video track as quality badges, select arrows right after the text.
(function (JT) {
    'use strict';

    // The badges sit over the select, which stays clickable underneath for picking another track.
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

    const canvas = document.createElement('canvas');

    // A select is as wide as its longest option; size it to the chosen one so the arrow follows the text.
    function fitSelect(select) {
        const container = select.closest('.selectContainer');
        const badges = container?.querySelector('.jt-video-badges');
        let width;
        if (badges) {
            badges.style.left = `${select.offsetLeft}px`;
            width = badges.getBoundingClientRect().width;
        } else {
            const context = canvas.getContext('2d');
            // The "font" shorthand is empty for selects in Safari, so it is built from its parts.
            const style = getComputedStyle(select);
            context.font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
            width = context.measureText(select.options[select.selectedIndex]?.text || '').width;
        }
        const target = `${Math.ceil(width) + 34}px`;
        if (select.style.width !== target) {
            select.style.width = target;
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

        document.querySelectorAll('.trackSelections .detailTrackSelect').forEach((select) => {
            if (!select.dataset.jtFit) {
                select.dataset.jtFit = 'true';
                select.addEventListener('change', () => fitSelect(select));
            }
            fitSelect(select);
        });

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
