// Item page tweaks: "age6+" as "6+".
(function (JT) {
    'use strict';

    function update() {
        // Runs on every DOM change: leave at once off item pages.
        if (!JT.config?.TweakItemPage || !JT.route().startsWith('#/details')) {
            return;
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
