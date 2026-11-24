// Managed collections as independent home rows, directly after Next Up when it is present.
(function (JT) {
    'use strict';

    let run = 0;
    let refreshedAt = 0;

    function visible(collection) {
        const settings = JT.settings || {};
        return settings.ShowPickCollections !== false
            && settings[collection.Key === 'themed' ? 'ShowThemedCollection' : 'ShowBestCollection'] !== false;
    }

    function home() {
        if (!/^#\/home(\.html)?(\?|$)|^$|^#\/?$/.test(JT.route())) {
            return null;
        }
        return JT.visiblePage('.homePage')?.querySelector('.homeSectionsContainer') || null;
    }

    function nextUp(container) {
        return Array.from(container.querySelectorAll(':scope > .verticalSection')).find((section) =>
            section.querySelector('a[href*="#/nextup"], a[href*="/nextup"], a[href*="type=nextup"]')) || null;
    }

    function detailsUrl(id, serverId = window.ApiClient.serverId()) {
        return `#/details?id=${encodeURIComponent(id)}&serverId=${encodeURIComponent(serverId)}`;
    }

    function imageUrl(item) {
        return JT.imageUrl(`Items/${item.Id}/Images/Primary`, {
            tag: item.ImageTags?.Primary || undefined,
            maxWidth: 360,
            quality: 90
        });
    }

    function card(item) {
        const id = item.Id;
        const serverId = item.ServerId || window.ApiClient.serverId();
        const href = detailsUrl(id, serverId);
        const image = item.ImageTags?.Primary
            ? JT.el('img', { className: 'cardImage', src: imageUrl(item), alt: '', loading: 'lazy' })
            : JT.el('span', { className: 'material-icons jt-pick-placeholder', 'aria-hidden': 'true' }, 'movie');
        const scalable = JT.el('div', { className: 'cardScalable' }, [
            JT.el('div', { className: 'cardPadder cardPadder-overflowPortrait' }),
            JT.el('a', {
                className: 'cardImageContainer cardContent itemAction',
                href,
                'data-action': 'link',
                'aria-label': item.Name || '',
                role: 'img'
            }, image)
        ]);
        const percentage = item.UserData?.PlayedPercentage;
        if (percentage > 0) {
            scalable.appendChild(JT.el('div', { className: 'jt-pick-progress', 'aria-hidden': 'true' },
                JT.el('span', { style: `width:${Math.min(100, Math.max(0, percentage))}%` })));
        }

        return JT.el('div', {
            className: 'card overflowPortraitCard card-withuserdata card-hoverable jt-pick-card',
            'data-id': id,
            'data-serverid': serverId,
            'data-type': item.Type || 'Movie',
            'data-mediatype': item.MediaType || 'Video',
            'data-isfolder': 'false'
        }, JT.el('div', { className: 'cardBox cardBox-bottompadded' }, [
            scalable,
            JT.el('div', { className: 'cardFooter cardFooter-transparent' },
                JT.el('div', { className: 'cardText cardTextCentered' },
                    JT.el('a', {
                        className: 'jt-pick-title itemAction textActionButton',
                        href,
                        'data-action': 'link',
                        text: item.Name || ''
                    })))
        ]));
    }

    async function load(section, collection, expectedRun) {
        const result = await JT.get(`Items?userId=${encodeURIComponent(JT.userId)}&ParentId=${collection.Id}`
            + '&IncludeItemTypes=Movie&Recursive=false&SortBy=SortName&SortOrder=Ascending&Limit=24'
            + '&EnableUserData=true&EnableImages=true&ImageTypeLimit=1&EnableImageTypes=Primary'
            + '&Fields=PrimaryImageAspectRatio').catch((error) => {
            console.error('[JellyTwiggy] collection row', error);
            return null;
        });
        if (expectedRun !== run || !section.isConnected) {
            return;
        }

        const items = result?.Items || [];
        if (!items.length) {
            section.remove();
            return;
        }

        const content = section.querySelector('.jt-pick-items');
        content.replaceChildren(...items.map(card));
        section.hidden = false;
        JT.scanCards?.([content]);
    }

    function place(container, section, previous) {
        const anchor = nextUp(container);
        if (anchor) {
            const expected = previous || anchor;
            if (section.previousElementSibling !== expected) {
                expected.insertAdjacentElement('afterend', section);
            }
            return section;
        }

        const latest = Array.from(container.querySelectorAll('.verticalSection')).find((candidate) =>
            candidate !== section && !candidate.classList.contains('jt-pick-section')
            && candidate.querySelector('a[href*="#/movies"], a[href*="#/tv"], a[href*="#/music"], a[href*="#/homevideos"]'));
        if (latest) {
            if (section.nextElementSibling !== latest) {
                latest.before(section);
            }
        } else if (container.lastElementChild !== section) {
            container.appendChild(section);
        }
        return section;
    }

    function update() {
        const container = home();
        if (!container) {
            document.querySelectorAll('.jt-pick-section').forEach((section) => section.remove());
            run++;
            return;
        }

        const collections = (JT.config?.PickCollections || []).filter(visible);
        const wanted = new Set(collections.map((collection) => collection.Key));
        container.querySelectorAll(':scope > .jt-pick-section').forEach((section) => {
            if (!wanted.has(section.dataset.jtPick)) {
                section.remove();
            }
        });

        let previous = null;
        for (const collection of collections) {
            let section = container.querySelector(`:scope > .jt-pick-section[data-jt-pick="${collection.Key}"]`);
            if (!section) {
                section = JT.el('div', {
                    className: 'verticalSection jt-pick-section',
                    'data-jt-pick': collection.Key,
                    hidden: true
                }, [
                    JT.el('div', { className: 'sectionTitleContainer sectionTitleContainer-cards padded-left' },
                        JT.el('a', {
                            is: 'emby-linkbutton',
                            className: 'more button-flat button-flat-mini sectionTitleTextButton',
                            href: detailsUrl(collection.Id)
                        }, [
                            JT.el('h2', { className: 'sectionTitle sectionTitle-cards', text: collection.Name }),
                            JT.icon('chevron_right')
                        ])),
                    JT.el('div', { className: 'itemsContainer scrollSlider focuscontainer-x jt-pick-items' })
                ]);
            }

            section.querySelector('h2').textContent = collection.Name;
            const titleLink = section.querySelector('.sectionTitleTextButton');
            titleLink.setAttribute('is', 'emby-linkbutton');
            titleLink.setAttribute('href', detailsUrl(collection.Id));
            previous = place(container, section, previous);
            if (section.dataset.jtLoaded !== `${JT.userId}:${collection.Id}`) {
                section.dataset.jtLoaded = `${JT.userId}:${collection.Id}`;
                load(section, collection, run);
            }
        }
    }

    async function refresh() {
        const now = Date.now();
        if (now - refreshedAt < 60000) {
            return;
        }
        refreshedAt = now;
        const config = await JT.get('JellyTwiggy/ClientConfig').catch(() => null);
        if (config && JT.config) {
            JT.config.PickCollections = config.PickCollections || [];
            update();
        }
    }

    JT.refreshPicks = update;

    JT.register({
        init() {
            JT.onDomChange(update);
            JT.onUserChange(() => {
                run++;
                refreshedAt = 0;
                update();
            });
            JT.onNavigate(() => {
                run++;
                update();
                if (home()) {
                    refresh();
                }
            });
        }
    });
})(window.JellyTwiggy);
