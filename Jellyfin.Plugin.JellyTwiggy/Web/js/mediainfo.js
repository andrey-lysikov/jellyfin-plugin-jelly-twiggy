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

    // Measures text with the select's own font, letter spacing and case: canvas drifts from the real rendering.
    const widths = new Map();

    function textWidth(select, text) {
        const style = getComputedStyle(select);
        const key = [style.font, style.fontFamily, style.fontSize, style.fontWeight, style.letterSpacing, style.textTransform, text].join('|');
        if (widths.has(key)) {
            return widths.get(key);
        }
        // A jt-* node: the DOM observer ignores it, so measuring never re-triggers itself.
        const probe = JT.el('span', { className: 'jt-measure', text });
        for (const name of ['fontStyle', 'fontWeight', 'fontSize', 'fontFamily', 'letterSpacing', 'textTransform', 'wordSpacing']) {
            probe.style[name] = style[name];
        }
        probe.style.cssText += ';position:absolute;visibility:hidden;white-space:pre;left:-9999px;top:0';
        document.body.appendChild(probe);
        const width = probe.getBoundingClientRect().width;
        probe.remove();
        widths.set(key, width);
        return width;
    }

    // A select is as wide as its longest option; size it to the chosen one, with room for the arrow on top.
    function fitSelect(select) {
        const container = select.closest('.selectContainer');
        const badges = container?.querySelector('.jt-video-badges');
        let width;
        if (badges) {
            badges.style.left = `${select.offsetLeft}px`;
            width = badges.getBoundingClientRect().width;
        } else {
            width = textWidth(select, select.options[select.selectedIndex]?.text || '');
        }
        const arrow = container?.querySelector('.selectArrowContainer')?.getBoundingClientRect().width || 24;
        const target = `${Math.ceil(width + arrow) + 14}px`;
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
            // Web fonts may arrive later and render wider than the fallback that was measured.
            document.fonts?.ready.then(() => {
                widths.clear();
                fitSelect(select);
            });
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
