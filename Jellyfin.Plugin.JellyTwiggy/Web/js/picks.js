// Home screen rows of the picks channels: just the channel name instead of jellyfin-web's "Latest: …".
(function (JT) {
    'use strict';

    function update() {
        const channels = JT.config?.PickChannels || [];
        if (!channels.length || !/^#\/home(\.html)?(\?|$)|^$|^#\/?$/.test(JT.route())) {
            return;
        }

        // The row title links to the channel; its id is in the link.
        document.querySelectorAll('.homeSectionsContainer .sectionTitleContainer a[href]').forEach((link) => {
            const href = link.getAttribute('href').toLowerCase();
            const channel = channels.find((c) => href.includes(JT.normalizeId(c.Id)));
            const title = channel && link.querySelector('.sectionTitle');
            if (title && title.textContent !== channel.Name) {
                title.textContent = channel.Name;
            }
        });
    }

    JT.register({
        init() {
            JT.onDomChange(update);
        }
    });
})(window.JellyTwiggy);
