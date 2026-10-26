// Per-user "hide favorites": ♥ buttons and filters are hidden by CSS, Favorites tabs are tagged here.
(function (JT) {
    'use strict';

    // jellyfin-web's "Favorites" tab label in the supported languages.
    const TAB_LABELS = new Set(['favorites', 'избранное', 'favoriten']);
    const TABS = '.emby-tab-button, [role="tab"]';

    function update() {
        const hide = !!JT.settings?.HideFavorites;
        document.documentElement.classList.toggle('jt-hide-favorites', hide);
        if (!hide) {
            return;
        }

        if (/^#\/home\?(.*&)?tab=1(&|$)/.test(JT.route())) {
            window.location.replace('#/home');
            return;
        }

        document.querySelectorAll(TABS).forEach((tab) => {
            if (tab.dataset.jtFavoritesTab || !TAB_LABELS.has(tab.textContent.trim().toLowerCase())) {
                return;
            }

            tab.dataset.jtFavoritesTab = 'true';
            // A hidden tab that is open (e.g. remembered as the last one) switches to the first tab.
            if (tab.classList.contains('emby-tab-button-active') || tab.getAttribute('aria-selected') === 'true') {
                tab.parentElement?.querySelector(`:is(${TABS}):not([data-jt-favorites-tab])`)?.click();
            }
        });
    }

    JT.register({
        init() {
            JT.onDomChange(update);
        }
    });
})(window.JellyTwiggy);
