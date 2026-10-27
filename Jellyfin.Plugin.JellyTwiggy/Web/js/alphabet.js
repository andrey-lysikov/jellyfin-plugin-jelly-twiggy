// Library alphabet picker: its letters come from the sort names of what the page lists, in any language.
(function (JT) {
    'use strict';

    const PICKER = '.alphaPicker.alphaPicker-fixed-right';
    const SOURCES = { getItems: 'Items', getArtists: 'Artists', getAlbumArtists: 'Artists/AlbumArtists', getPeople: 'Persons' };
    const OTHER = '#';
    const LETTER = /^\p{L}/u;
    const cache = new Map(); // query -> letters
    // Paging, the letter filter and DTO shaping don't change which letters exist.
    const SKIPPED = new Set(['StartIndex', 'Limit', 'NameStartsWith', 'NameLessThan', 'NameStartsWithOrGreater', 'Fields',
        'EnableImages', 'EnableImageTypes', 'ImageTypeLimit', 'EnableUserData', 'EnableTotalRecordCount']);
    const COMPACT_FROM = 28;

    const listings = new Map(); // route -> latest library listing { key, params }
    let lastListing = null;

    function remember(source, query) {
        // Only paged, ordered listings are library pages; shuffle and home rows are not.
        if (!query || !query.SortBy || /Random/i.test(query.SortBy) || (query.StartIndex === undefined && query.Limit === undefined)) {
            return;
        }

        const params = new URLSearchParams();
        for (const [key, value] of Object.entries(query)) {
            if (value !== null && value !== undefined && value !== '' && !SKIPPED.has(key) && key !== 'SortBy' && key !== 'SortOrder') {
                params.append(key, Array.isArray(value) ? value.join(',') : String(value));
            }
        }
        // Letters follow the page's order when it sorts by name, otherwise ascending.
        const byName = /^(SortName|Name)\b/i.test(String(query.SortBy));
        const descending = byName && /^Descending/i.test(String(query.SortOrder || ''));
        params.set('SortBy', 'SortName');
        params.set('SortOrder', descending ? 'Descending' : 'Ascending');
        const key = `${source}?${params}`;
        lastListing = { key, source, params };
        listings.set(JT.route(), lastListing);
    }

    // Library pages keep their query private, so it is read from the ApiClient call that loads the list.
    function patchApiClient() {
        const proto = window.ApiClient && Object.getPrototypeOf(window.ApiClient);
        if (!proto || proto.jtAlphabetPatched) {
            return;
        }

        proto.jtAlphabetPatched = true;
        for (const [method, source] of Object.entries(SOURCES)) {
            const original = proto[method];
            if (typeof original === 'function') {
                proto[method] = function (userId, query) {
                    try {
                        remember(source, query);
                    } catch (e) {
                        console.error('[JellyTwiggy] alphabet', e);
                    }
                    return original.apply(this, arguments);
                };
            }
        }
    }

    // The browser asks Jellyfin itself, the same way the page does, so it works behind any reverse proxy.
    async function loadLetters(listing) {
        if (cache.has(listing.key)) {
            return cache.get(listing.key);
        }

        const params = new URLSearchParams(listing.params);
        params.set('userId', JT.userId);
        params.set('Fields', 'SortName');
        params.set('EnableImages', 'false');
        params.set('EnableUserData', 'false');
        params.set('EnableTotalRecordCount', 'false');
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

    // Buttons keep jellyfin-web's classes and data-value, so its own click handling applies the letter.
    function render(picker, letters) {
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
        picker.classList.add('jt-alpha');
        picker.classList.toggle('jt-alpha-compact', letters.length >= COMPACT_FROM);
    }

    async function update() {
        patchApiClient();
        const picker = Array.from(document.querySelectorAll(PICKER)).find((element) => element.offsetParent !== null);
        const listing = listings.get(JT.route()) || lastListing;
        if (!picker || !listing || picker.dataset.jtLetters === listing.key) {
            return;
        }

        picker.dataset.jtLetters = listing.key;
        try {
            const letters = await loadLetters(listing);
            if (picker.dataset.jtLetters === listing.key) {
                render(picker, letters);
            }
        } catch (e) {
            console.error('[JellyTwiggy] alphabet letters', e);
            delete picker.dataset.jtLetters;
        }
    }

    JT.register({
        init() {
            patchApiClient();
            JT.onDomChange(update);
        }
    });
})(window.JellyTwiggy);
