// User settings: an entry in the profile preferences menu and an in-page settings form.
(function (JT) {
    'use strict';

    const SECTIONS = [
        { title: 'settingsSkipSection', hint: 'autoSkipHint', keys: ['AutoSkipIntro', 'AutoSkipOutro'] },
        { title: 'settingsPlayerSection', hint: 'autoPipHint', keys: ['AutoPauseOnTabSwitch', 'AutoResumeOnTabSwitch', 'AutoPipOnTabSwitch', 'LongPressSpeedUp'] },
        { title: 'settingsHandoffSection', hint: 'enableHandoffHint', keys: ['EnableHandoff'] },
        { title: 'settingsInterfaceSection', keys: ['HideFavorites', 'HideCast', 'HideWatchedFromResume', 'CardRadius', 'UiRadius'] },
        { title: 'settingsCollectionsSection', keys: ['ShowPickCollections', 'ShowThemedCollection', 'ShowBestCollection'] },
        { title: 'settingsSyncSection', hint: 'syncHint', keys: ['SyncJellyfinSettings'] }
    ];

    const CHOICES = { CardRadius: ['none', 'default', 'large'], UiRadius: ['none', 'default', 'large'] };
    const labelKey = (key) => key.charAt(0).toLowerCase() + key.slice(1);
    const isOpen = () => JT.route().startsWith('#/mypreferencesmenu') && JT.routeParam('jellytwiggy') === '1';

    function inputElement(tag, type) {
        const input = document.createElement(tag, { is: type });
        input.setAttribute('is', type);
        return input;
    }

    function checkbox(key, checked) {
        const input = inputElement('input', 'emby-checkbox');
        input.type = 'checkbox';
        input.checked = checked;
        input.id = `jt-setting-${key}`;
        const row = JT.el('div', { className: 'checkboxContainer' },
            JT.el('label', { for: input.id }, [
                input,
                JT.el('span', { text: JT.t(labelKey(key)) })
            ]));
        return { row, input };
    }

    function choice(key, value) {
        const select = inputElement('select', 'emby-select');
        select.className = 'emby-select';
        select.id = `jt-setting-${key}`;
        select.setAttribute('aria-label', JT.t(labelKey(key)));
        for (const option of CHOICES[key]) {
            select.appendChild(JT.el('option', { value: option, selected: option === (value || 'default'), text: JT.t(`radius_${option}`) }));
        }
        return {
            row: JT.el('div', { className: 'selectContainer jt-setting-choice' },
                JT.el('label', { className: 'selectLabel', for: select.id, text: JT.t(labelKey(key)) })),
            input: select
        };
    }

    function choiceRow(key, value) {
        const result = choice(key, value);
        result.row.appendChild(result.input);
        return result;
    }

    function collectionState(inputs) {
        const enabled = inputs.ShowPickCollections.checked;
        for (const key of ['ShowThemedCollection', 'ShowBestCollection']) {
            inputs[key].disabled = !enabled;
        }
    }

    function build(page, settings) {
        const inputs = {};
        const content = JT.el('div', { className: 'readOnlyContent jt-settings-content' });
        for (const section of SECTIONS) {
            const block = JT.el('div', { className: 'verticalSection verticalSection-extrabottompadding' },
                JT.el('h3', { className: 'sectionTitle', text: JT.t(section.title) }));
            for (const key of section.keys) {
                const field = CHOICES[key]
                    ? choiceRow(key, settings[key])
                    : checkbox(key, settings[key]);
                inputs[key] = field.input;
                block.appendChild(field.row);
            }
            if (section.hint) {
                block.appendChild(JT.el('p', { className: 'fieldDescription', text: JT.t(section.hint) }));
            }
            content.appendChild(block);
        }

        const cancel = JT.el('button', {
            type: 'button',
            className: 'emby-button button-flat',
            text: JT.t('cancel'),
            onClick: () => { window.location.hash = '#/mypreferencesmenu'; }
        });
        const save = JT.el('button', {
            type: 'submit',
            className: 'raised button-submit emby-button',
            text: JT.t('save')
        });
        const actions = JT.el('div', { className: 'jt-settings-actions' }, [cancel, save]);
        const form = JT.el('form', { className: 'jt-settings-form' }, [content, actions]);
        const root = JT.el('div', { className: 'jt-settings-page padded-left padded-right padded-bottom-page' }, [
            JT.el('h2', { className: 'sectionTitle', text: JT.t('settingsTitle') }),
            form
        ]);
        page.appendChild(root);

        inputs.ShowPickCollections.addEventListener('change', () => collectionState(inputs));
        collectionState(inputs);
        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            const updated = { ...settings };
            for (const [key, input] of Object.entries(inputs)) {
                updated[key] = CHOICES[key] ? input.value : input.checked;
            }

            save.disabled = true;
            try {
                await JT.post('JellyTwiggy/UserSettings', updated);
                const syncTurnedOn = updated.SyncJellyfinSettings && !settings.SyncJellyfinSettings;
                JT.settings = updated;
                JT.applyClasses();
                JT.refreshPicks?.();
                if (syncTurnedOn) {
                    JT.syncSettingsNow?.();
                }
                JT.toast(JT.t('saved'));
                window.location.hash = '#/mypreferencesmenu';
            } catch (error) {
                console.error('[JellyTwiggy] settings', error);
                JT.toast(JT.t('saveFailed'));
                save.disabled = false;
            }
        });
    }

    function restore(page) {
        page.querySelector(':scope > .jt-settings-page')?.remove();
        Array.from(page.children).forEach((child) => { child.hidden = false; });
    }

    function render() {
        const page = JT.visiblePage('.userPreferencesPage');
        if (!page) {
            return;
        }

        if (!isOpen()) {
            restore(page);
            return;
        }

        if (page.querySelector(':scope > .jt-settings-page')) {
            return;
        }

        Array.from(page.children).forEach((child) => { child.hidden = true; });
        const currentRun = JT.userId;
        JT.reloadSettings().then((settings) => {
            if (isOpen() && JT.userId === currentRun && page.isConnected) {
                build(page, settings);
            } else if (page.isConnected) {
                restore(page);
            }
        }).catch((error) => {
            console.error('[JellyTwiggy] settings', error);
            restore(page);
        });
    }

    function addMenuEntry() {
        const page = JT.visiblePage('.userPreferencesPage');
        if (!page || page.querySelector('.jt-prefs-link')) {
            return;
        }

        const anchor = page.querySelector('.lnkSubtitlePreferences') || page.querySelector('.lnkPlaybackPreferences');
        if (!anchor) {
            return;
        }

        const link = JT.el('a', {
            href: '#/mypreferencesmenu?jellytwiggy=1',
            className: 'listItem-border jt-prefs-link'
        }, JT.el('div', { className: 'listItem' }, [
            JT.icon('tune', 'listItemIcon listItemIcon-transparent'),
            JT.el('div', { className: 'listItemBody' }, JT.el('div', { className: 'listItemBodyText', text: JT.t('menuEntry') }))
        ]));
        anchor.insertAdjacentElement('afterend', link);
    }

    JT.register({
        init() {
            JT.onDomChange(() => {
                addMenuEntry();
                render();
            });
            JT.onNavigate(render);
            JT.onUserChange(render);
        }
    });
})(window.JellyTwiggy);
