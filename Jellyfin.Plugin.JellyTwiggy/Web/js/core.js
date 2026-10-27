// Jelly Twiggy core: API access, page tracking, DOM helpers and module startup.
(function (JT) {
    'use strict';

    const modules = [];
    const domListeners = [];
    const navigationListeners = [];
    const userListeners = [];
    const changedRoots = new Set();
    let domScheduled = false;
    let fullPass = false;

    JT.config = null;
    JT.settings = null;

    // ---- API -------------------------------------------------------------------------------

    function authHeader() {
        return `MediaBrowser Token="${window.ApiClient.accessToken()}"`;
    }

    // Requests a server path (relative to the server root, e.g. "JellyTwiggy/Sessions").
    JT.request = async function (method, path, body) {
        const response = await fetch(window.ApiClient.getUrl(path), {
            method,
            headers: {
                'Authorization': authHeader(),
                'Content-Type': 'application/json'
            },
            body: body === undefined ? undefined : JSON.stringify(body)
        });
        if (!response.ok) {
            throw new Error(`${method} ${path}: ${response.status}`);
        }
        return response.status === 204 ? null : response.json();
    };

    JT.get = (path) => JT.request('GET', path);
    JT.post = (path, body) => JT.request('POST', path, body);

    JT.imageUrl = function (path, params) {
        return window.ApiClient.getUrl(path, params);
    };

    // Jellyfin uses dashless ids in the DOM and dashed ones in JSON dictionary keys.
    JT.normalizeId = (id) => (id || '').replace(/-/g, '').toLowerCase();

    // ---- DOM helpers -----------------------------------------------------------------------

    JT.el = function (tag, attributes, children) {
        const element = document.createElement(tag);
        for (const [name, value] of Object.entries(attributes || {})) {
            if (value === undefined || value === null || value === false) {
                continue;
            }
            if (name === 'className') {
                element.className = value;
            } else if (name === 'text') {
                element.textContent = value;
            } else if (name.startsWith('on')) {
                element.addEventListener(name.slice(2).toLowerCase(), value);
            } else {
                element.setAttribute(name, value === true ? '' : value);
            }
        }
        for (const child of [].concat(children || [])) {
            if (child !== null && child !== undefined && child !== false) {
                element.append(child instanceof Node ? child : String(child));
            }
        }
        return element;
    };

    JT.icon = (name, className) => JT.el('span', { className: `material-icons ${name} ${className || ''}`, 'aria-hidden': 'true' });

    JT.toast = function (text) {
        const toast = JT.el('div', { className: 'jt-toast', text });
        document.body.appendChild(toast);
        requestAnimationFrame(() => toast.classList.add('jt-toast-visible'));
        setTimeout(() => {
            toast.classList.remove('jt-toast-visible');
            setTimeout(() => toast.remove(), 300);
        }, 2500);
    };

    JT.formatDuration = function (seconds) {
        const totalMinutes = Math.round((seconds || 0) / 60);
        const h = Math.floor(totalMinutes / 60);
        const m = totalMinutes % 60;
        return h > 0 ? JT.t('hoursMinutes', { h, m }) : JT.t('minutes', { m });
    };

    JT.formatTicks = function (ticks) {
        const total = Math.floor((ticks || 0) / 1e7);
        const h = Math.floor(total / 3600);
        const m = Math.floor((total % 3600) / 60);
        const s = total % 60;
        const pad = (n) => String(n).padStart(2, '0');
        return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
    };

    JT.formatDate = function (value) {
        if (!value) {
            return '';
        }
        // Server dates are UTC; old ones may come without a zone suffix.
        const date = new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : value + 'Z');
        return date.toLocaleString(JT.language(), { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    };

    JT.formatRelative = function (value) {
        const date = new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : value + 'Z');
        const seconds = Math.round((date.getTime() - Date.now()) / 1000);
        const format = new Intl.RelativeTimeFormat(JT.language(), { numeric: 'auto' });
        const abs = Math.abs(seconds);
        if (abs < 60) {
            return format.format(seconds, 'second');
        }
        if (abs < 3600) {
            return format.format(Math.round(seconds / 60), 'minute');
        }
        if (abs < 86400) {
            return format.format(Math.round(seconds / 3600), 'hour');
        }
        return format.format(Math.round(seconds / 86400), 'day');
    };

    // Avatar <img> with an initials fallback. imageTag null means "no avatar";
    // undefined means "unknown", so the image is tried without a tag.
    JT.avatar = function (userId, userName, imageTag, className) {
        const initials = JT.el('span', { className: `jt-avatar ${className || ''}`, text: (userName || '?').slice(0, 1).toUpperCase() });
        let hue = 0;
        for (const ch of String(userId)) {
            hue = (hue * 31 + ch.charCodeAt(0)) % 360;
        }
        initials.style.backgroundColor = `hsl(${hue}, 45%, 40%)`;
        if (imageTag === null || imageTag === '') {
            return initials;
        }

        const params = { quality: 90, maxHeight: 96 };
        if (imageTag) {
            params.tag = imageTag;
        }
        const img = JT.el('img', {
            className: `jt-avatar ${className || ''}`,
            alt: userName || '',
            loading: 'lazy',
            src: JT.imageUrl(`Users/${userId}/Images/Primary`, params)
        });
        img.addEventListener('error', () => img.replaceWith(initials), { once: true });
        return img;
    };

    // Minimal modal dialog. Returns { root, body, footer, close }.
    JT.dialog = function (title, onClose) {
        const body = JT.el('div', { className: 'jt-dialog-body' });
        const footer = JT.el('div', { className: 'jt-dialog-footer' });
        const closeButton = JT.el('button', { type: 'button', className: 'jt-icon-button', title: JT.t('close') }, JT.icon('close'));
        const dialog = JT.el('div', { className: 'jt-dialog', role: 'dialog', 'aria-modal': 'true' }, [
            JT.el('div', { className: 'jt-dialog-header' }, [JT.el('h2', { text: title }), closeButton]),
            body,
            footer
        ]);
        const root = JT.el('div', { className: 'jt-dialog-backdrop' }, dialog);

        function close() {
            root.remove();
            document.removeEventListener('keydown', onKey, true);
            onClose?.();
        }

        function onKey(e) {
            if (e.key === 'Escape') {
                e.stopPropagation();
                close();
            }
        }

        closeButton.addEventListener('click', close);
        root.addEventListener('mousedown', (e) => {
            if (e.target === root) {
                close();
            }
        });
        document.addEventListener('keydown', onKey, true);
        document.body.appendChild(root);
        return { root, body, footer, close };
    };

    JT.toggle = function (label, checked, hint) {
        const input = JT.el('input', { type: 'checkbox', checked: !!checked });
        const row = JT.el('label', { className: 'jt-toggle' }, [
            input,
            JT.el('span', { className: 'jt-toggle-track' }, JT.el('span', { className: 'jt-toggle-thumb' })),
            JT.el('span', { className: 'jt-toggle-text' }, [
                JT.el('span', { text: label }),
                hint ? JT.el('span', { className: 'jt-hint', text: hint }) : null
            ])
        ]);
        return { row, input };
    };

    // ---- Page tracking ---------------------------------------------------------------------

    // Called (throttled to one animation frame) after DOM changes with the changed subtrees,
    // or with null for a full pass (navigation, user switch).
    JT.onDomChange = (fn) => domListeners.push(fn);

    // Called after another user logged in, once their configuration and settings are loaded.
    JT.onUserChange = (fn) => userListeners.push(fn);

    // Called on route changes with the current hash.
    JT.onNavigate = (fn) => navigationListeners.push(fn);

    JT.route = () => window.location.hash || '';

    JT.routeParam = function (name) {
        const query = JT.route().split('?')[1] || '';
        return new URLSearchParams(query).get(name);
    };

    // The visible instance of a legacy page (jellyfin-web keeps hidden copies of recent views).
    JT.visiblePage = function (selector) {
        return Array.from(document.querySelectorAll(selector)).find((page) => !page.classList.contains('hide')) || null;
    };

    function runDomListeners() {
        domScheduled = false;
        const roots = fullPass ? null : Array.from(changedRoots);
        fullPass = false;
        changedRoots.clear();
        for (const fn of domListeners) {
            try {
                fn(roots);
            } catch (e) {
                console.error('[JellyTwiggy]', e);
            }
        }
    }

    function scheduleDom(full) {
        fullPass = fullPass || full === true;
        if (!domScheduled) {
            domScheduled = true;
            requestAnimationFrame(runDomListeners);
        }
    }

    // Our own badges, buttons and panels: adding or removing them must not wake the listeners again.
    const isOwnNode = (node) => node.nodeType === Node.ELEMENT_NODE
        && (node.id?.startsWith('jt-') || Array.from(node.classList).some((name) => name.startsWith('jt-')));

    function onMutations(records) {
        for (const record of records) {
            if (record.type === 'attributes') {
                changedRoots.add(record.target);
                continue;
            }
            const added = Array.from(record.addedNodes).filter((node) => !isOwnNode(node));
            const removedForeign = Array.from(record.removedNodes).some((node) => !isOwnNode(node));
            for (const node of added) {
                changedRoots.add(node.nodeType === Node.ELEMENT_NODE ? node : record.target);
            }
            if (removedForeign) {
                changedRoots.add(record.target);
            }
        }
        if (changedRoots.size > 0) {
            scheduleDom();
        }
    }

    function runNavigation() {
        const route = JT.route();
        for (const fn of navigationListeners) {
            try {
                fn(route);
            } catch (e) {
                console.error('[JellyTwiggy]', e);
            }
        }
        scheduleDom(true);
    }

    // ---- Startup ---------------------------------------------------------------------------

    JT.register = (module) => modules.push(module);

    JT.reloadSettings = async function () {
        JT.settings = await JT.get('JellyTwiggy/UserSettings');
        return JT.settings;
    };

    function waitForLogin() {
        return new Promise((resolve) => {
            const check = () => {
                const client = window.ApiClient;
                if (client && client.accessToken && client.accessToken() && client.getCurrentUserId && client.getCurrentUserId()) {
                    resolve(client.getCurrentUserId());
                } else {
                    setTimeout(check, 500);
                }
            };
            check();
        });
    }

    // Admin options and user settings switch CSS through classes on <html>.
    JT.applyClasses = function () {
        const root = document.documentElement;
        const config = JT.config || {};
        const settings = JT.settings || {};
        root.classList.toggle('jt-tweak-item', !!config.TweakItemPage);
        root.classList.toggle('jt-tweak-player', !!config.TweakPlayer);
        root.classList.toggle('jt-anim', !!config.EnableAnimations);
        root.classList.toggle('jt-style-cards', !!config.StyleCards);
        root.classList.toggle('jt-hide-cast', !!settings.HideCast);
        for (const [prefix, value] of [['jt-card-radius-', settings.CardRadius], ['jt-ui-radius-', settings.UiRadius]]) {
            for (const option of ['none', 'large']) {
                root.classList.toggle(prefix + option, value === option);
            }
        }
    };

    // The new user's id, config and settings are published together, so no module ever sees a mix.
    async function loadUserState() {
        const userId = await waitForLogin();
        const config = await JT.get('JellyTwiggy/ClientConfig');
        const settings = await JT.get('JellyTwiggy/UserSettings');
        const switched = !!JT.userId && JT.userId !== userId;
        JT.userId = userId;
        JT.config = config;
        JT.settings = settings;
        JT.applyClasses();
        if (switched) {
            for (const fn of userListeners) {
                try {
                    fn(userId);
                } catch (e) {
                    console.error('[JellyTwiggy]', e);
                }
            }
        }
    }

    async function start() {
        try {
            await loadUserState();
        } catch (e) {
            console.error('[JellyTwiggy] failed to load configuration', e);
            setTimeout(start, 10000);
            return;
        }

        for (const module of modules) {
            try {
                module.init?.();
            } catch (e) {
                console.error('[JellyTwiggy]', e);
            }
        }

        // data-id changes catch cards that virtual lists reuse for another item.
        new MutationObserver(onMutations).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-id'] });
        window.addEventListener('hashchange', runNavigation);
        document.addEventListener('viewshow', runNavigation);
        runNavigation();

        // Another user logged in: switch to their configuration and settings.
        setInterval(async () => {
            const current = window.ApiClient?.getCurrentUserId?.();
            if (current && current !== JT.userId) {
                try {
                    await loadUserState();
                    runNavigation();
                } catch (e) {
                    console.error('[JellyTwiggy] failed to load configuration', e);
                }
            }
        }, 3000);
    }

    JT.start = start;
})(window.JellyTwiggy);
