// Jelly Twiggy client entry point, injected into jellyfin-web's index.html.
// Loads the stylesheet and the feature modules, then starts them.
(function () {
    'use strict';

    if (window.JellyTwiggy) {
        return;
    }

    const script = document.currentScript;
    const scriptUrl = new URL(script.src, window.location.href);
    const version = scriptUrl.searchParams.get('v') || '';
    const base = scriptUrl.href.replace(/js\/jelly-twiggy\.js.*$/, '');
    const modules = ['i18n', 'core', 'badges', 'player', 'settings', 'streams', 'watchers', 'dashboard', 'favorites'];

    const versioned = (path) => `${base}${path}?v=${encodeURIComponent(version)}`;

    window.JellyTwiggy = { version, asset: versioned };

    document.head.appendChild(Object.assign(document.createElement('link'), {
        rel: 'stylesheet',
        href: versioned('css/jelly-twiggy.css')
    }));

    function load(index) {
        if (index === modules.length) {
            console.info(`[JellyTwiggy] loaded v${version}`);
            window.JellyTwiggy.start();
            return;
        }

        const element = document.createElement('script');
        element.src = versioned(`js/${modules[index]}.js`);
        element.onload = () => load(index + 1);
        element.onerror = () => console.error(`[JellyTwiggy] failed to load ${modules[index]}.js`);
        document.head.appendChild(element);
    }

    load(0);
})();
