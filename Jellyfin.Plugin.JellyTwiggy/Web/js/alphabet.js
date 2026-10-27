// Library alphabet picker: its letters come from the sort names of what the page lists, in any language.
(function (JT) {
    'use strict';

    // Legacy layout: buttons of alphaPicker.js; modern layout: an MUI box with fixed A-Z toggle buttons.
    const LEGACY_PICKER = '.alphaPicker.alphaPicker-fixed-right';
    const MODERN_PICKER = '.alphaPicker-fixed-right:not(.alphaPicker)';
    const SOURCES = /\/(?:Users\/[^/]+\/)?(Items|Artists\/AlbumArtists|Artists|Persons)$/i;
    const OTHER = '#';
    const LETTER = /^\p{L}/u;
    const cache = new Map(); // query -> letters
    // Paging, the letter filter and DTO shaping don't change which letters exist.
    const SKIPPED = new Set(['startindex', 'limit', 'namestartswith', 'namelessthan', 'namestartswithorgreater', 'fields',
        'enableimages', 'enableimagetypes', 'imagetypelimit', 'enableuserdata', 'enabletotalrecordcount',
        'sortby', 'sortorder', 'userid', 'api_key']);
    const COMPACT_FROM = 28;
    const RETRY_MS = 30000;
    const failures = new Map(); // query -> time after which a failed load may be retried

    const listings = new Map(); // route -> latest library listing { key, source, params }
    let modernValue = null; // letter chosen in our modern picker

    // A paged, ordered list request is the page's listing; shuffles, home rows and our own requests are not.
    function remember(url) {
        let parsed;
        try {
            parsed = new URL(url, window.location.href);
        } catch {
            return;
        }
        const match = SOURCES.exec(parsed.pathname);
        const query = new Map(Array.from(parsed.searchParams.keys()).map((key) => [key.toLowerCase(), parsed.searchParams.getAll(key).join(',')]));
        const sortBy = query.get('sortby');
        if (!match || !sortBy || /Random/i.test(sortBy) || !query.has('startindex')) {
            return;
        }

        const params = new URLSearchParams();
        for (const [key, value] of query) {
            if (value !== '' && !SKIPPED.has(key)) {
                params.append(key, value);
            }
        }
        // Letters follow the page's order when it sorts by name, otherwise ascending.
        const descending = /^(SortName|Name)\b/i.test(sortBy) && /^Descending/i.test(query.get('sortorder') || '');
        params.set('sortBy', 'SortName');
        params.set('sortOrder', descending ? 'Descending' : 'Ascending');
        const source = /^Items$/i.test(match[1]) ? 'Items' : match[1];
        listings.set(JT.route(), { key: `${source}?${params}`, source, params });
    }

    // Library pages keep their query private (legacy) or behind the SDK (modern): the request URL tells it.
    function watchRequests() {
        const originalFetch = window.fetch;
        window.fetch = function (input) {
            try {
                remember(typeof input === 'string' ? input : input?.url);
            } catch (e) {
                console.error('[JellyTwiggy] alphabet', e);
            }
            return originalFetch.apply(this, arguments);
        };
        const originalOpen = XMLHttpRequest.prototype.open;
        XMLHttpRequest.prototype.open = function (method, url) {
            try {
                if (/^GET$/i.test(method)) {
                    remember(String(url));
                }
            } catch (e) {
                console.error('[JellyTwiggy] alphabet', e);
            }
            return originalOpen.apply(this, arguments);
        };
    }

    // The browser asks Jellyfin itself, the same way the page does, so it works behind any reverse proxy.
    async function loadLetters(listing) {
        if (cache.has(listing.key)) {
            return cache.get(listing.key);
        }

        const params = new URLSearchParams(listing.params);
        params.set('userId', JT.userId);
        params.set('fields', 'SortName');
        params.set('enableImages', 'false');
        params.set('enableUserData', 'false');
        params.set('enableTotalRecordCount', 'false');
        const result = await JT.get(`${listing.source}?${params}`);

        // SortName is lower case; the first character keeps its order of appearance.
        const values = [];
        for (const item of result?.Items || []) {
            const first = Array.from((item.SortName || item.Name || '').trim())[0];
            const value = !first ? null : LETTER.test(first) ? first : OTHER;
            if (value && !values.includes(value)) {
                values.push(value);
            }
        }
        // Turkish has dotted and dotless i: show them as İ and I instead of two identical I.
        const turkish = values.includes('ı');
        const letters = values.map((value) => ({
            value,
            label: value === 'i' && turkish ? 'İ' : value === 'ı' ? 'I' : value.toLocaleUpperCase()
        }));
        cache.set(listing.key, letters);
        return letters;
    }

    // Legacy buttons keep jellyfin-web's classes and data-value, so its own click handling applies the letter.
    function renderLegacy(picker, letters) {
        const row = picker.querySelector('.alphaPickerRow') || picker;
        const sample = row.querySelector('.alphaPickerButton');
        const className = (sample?.className || 'alphaPickerButton alphaPickerButton-vertical').replace(/\s*alphaPickerButton-selected/g, '');
        const selected = row.querySelector('.alphaPickerButton-selected')?.getAttribute('data-value');

        row.replaceChildren(...letters.map((letter) => JT.el('button', {
            type: 'button',
            className: letter.value === selected ? `${className} alphaPickerButton-selected` : className,
            'data-value': letter.value,
            text: letter.label
        })));
    }

    // The props of the React AlphabetPicker ({ value, onChange }) above the modern picker box.
    function modernProps(box) {
        const key = Object.keys(box).find((name) => name.startsWith('__reactFiber$'));
        let fiber = key && box[key];
        for (let depth = 0; fiber && depth < 12; depth++, fiber = fiber.return) {
            const props = fiber.memoizedProps;
            if (props && typeof props.onChange === 'function' && Object.keys(props).every((name) => name === 'value' || name === 'onChange')) {
                return props;
            }
        }
        return null;
    }

    function paintModern(list) {
        list.querySelectorAll('.jt-alpha-button').forEach((button) => {
            button.classList.toggle('jt-alpha-selected', button.dataset.value === modernValue);
        });
    }

    // Modern buttons are ours, next to the hidden React ones; a click goes to the page's own onChange.
    function renderModern(box, letters) {
        modernValue = modernProps(box)?.value ?? null;
        const list = JT.el('div', { className: 'jt-alpha-list' }, letters.map((letter) => JT.el('button', {
            type: 'button',
            className: 'jt-alpha-button',
            'data-value': letter.value,
            text: letter.label,
            onClick: () => {
                const props = modernProps(box);
                if (!props) {
                    return;
                }
                modernValue = modernValue === letter.value ? null : letter.value;
                props.onChange(modernValue);
                paintModern(list);
            }
        })));
        box.querySelector(':scope > .jt-alpha-list')?.remove();
        box.appendChild(list);
        paintModern(list);
    }

    // Pickers are position: fixed, so offsetParent is always null; hidden pages and tabs are told by class.
    const isShown = (picker) => !picker.closest('.hide, .pageTabContent:not(.is-active)');

    async function update() {
        const pickers = Array.from(document.querySelectorAll(`${LEGACY_PICKER}, ${MODERN_PICKER}`));
        const picker = pickers.find(isShown);
        // Another page's listing never applies here.
        const listing = listings.get(JT.route());
        if (!picker || !listing || picker.dataset.jtLetters === listing.key || (failures.get(listing.key) || 0) > Date.now()) {
            return;
        }

        picker.dataset.jtLetters = listing.key;
        try {
            const letters = await loadLetters(listing);
            failures.delete(listing.key);
            if (picker.dataset.jtLetters !== listing.key || letters.length === 0) {
                return;
            }
            if (picker.matches(LEGACY_PICKER)) {
                renderLegacy(picker, letters);
            } else {
                renderModern(picker, letters);
            }
            picker.classList.add('jt-alpha');
            picker.classList.toggle('jt-alpha-compact', letters.length >= COMPACT_FROM);
        } catch (e) {
            console.error('[JellyTwiggy] alphabet letters', e);
            failures.set(listing.key, Date.now() + RETRY_MS);
            delete picker.dataset.jtLetters;
        }
    }

    JT.register({
        init() {
            watchRequests();
            JT.onDomChange(update);
        }
    });
})(window.JellyTwiggy);
