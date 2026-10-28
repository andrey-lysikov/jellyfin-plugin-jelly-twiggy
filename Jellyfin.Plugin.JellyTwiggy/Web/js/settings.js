// User settings: an entry in the profile preferences menu that opens the Jelly Twiggy dialog.
(function (JT) {
    'use strict';

    const SECTIONS = [
        { title: 'settingsSkipSection', hint: 'autoSkipHint', keys: ['AutoSkipIntro', 'AutoSkipOutro'] },
        { title: 'settingsPlayerSection', hint: 'autoPipHint', keys: ['AutoPauseOnTabSwitch', 'AutoResumeOnTabSwitch', 'AutoPipOnTabSwitch', 'LongPressSpeedUp'] },
        { title: 'settingsHandoffSection', hint: 'enableHandoffHint', keys: ['EnableHandoff'] },
        { title: 'settingsInterfaceSection', keys: ['HideFavorites', 'HideCast', 'CardRadius', 'UiRadius'] },
        { title: 'settingsSyncSection', hint: 'syncHint', keys: ['SyncJellyfinSettings'] }
    ];

    const labelKey = (key) => key.charAt(0).toLowerCase() + key.slice(1);
    const CHOICES = { CardRadius: ['none', 'default', 'large'], UiRadius: ['none', 'default', 'large'] };

    function choiceRow(key, value) {
        const select = JT.el('select', { className: 'jt-select', 'aria-label': JT.t(labelKey(key)) },
            CHOICES[key].map((option) => JT.el('option', { value: option, selected: option === (value || 'default'), text: JT.t(`radius_${option}`) })));
        return { row: JT.el('label', { className: 'jt-choice' }, [JT.el('span', { text: JT.t(labelKey(key)) }), select]), input: select };
    }

    async function openDialog() {
        const settings = await JT.reloadSettings().catch(() => JT.settings || {});
        const dialog = JT.dialog(JT.t('settingsTitle'));
        const inputs = {};

        for (const section of SECTIONS) {
            dialog.body.appendChild(JT.el('h3', { className: 'jt-section-title', text: JT.t(section.title) }));
            for (const key of section.keys) {
                const toggle = CHOICES[key] ? choiceRow(key, settings[key]) : JT.toggle(JT.t(labelKey(key)), settings[key]);
                inputs[key] = toggle.input;
                dialog.body.appendChild(toggle.row);
            }
            if (section.hint) {
                dialog.body.appendChild(JT.el('p', { className: 'jt-hint', text: JT.t(section.hint) }));
            }
        }

        const save = JT.el('button', { type: 'button', className: 'jt-button jt-button-primary', text: JT.t('save') });
        const cancel = JT.el('button', { type: 'button', className: 'jt-button', text: JT.t('cancel'), onClick: () => dialog.close() });
        save.addEventListener('click', async () => {
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
                if (syncTurnedOn) {
                    JT.syncSettingsNow?.();
                }
                JT.toast(JT.t('saved'));
                dialog.close();
            } catch (e) {
                console.error('[JellyTwiggy] settings', e);
                JT.toast(JT.t('saveFailed'));
                save.disabled = false;
            }
        });
        dialog.footer.append(cancel, save);
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
            href: '#',
            className: 'emby-button listItem-border jt-prefs-link',
            style: 'display:block;margin:0;padding:0',
            onClick: (e) => {
                e.preventDefault();
                openDialog();
            }
        }, JT.el('div', { className: 'listItem' }, [
            JT.el('span', { className: 'listItemIcon listItemIcon-transparent jt-mono-icon', 'aria-hidden': 'true' }),
            JT.el('div', { className: 'listItemBody' }, JT.el('div', { className: 'listItemBodyText', text: JT.t('menuEntry') }))
        ]));
        anchor.insertAdjacentElement('afterend', link);
    }

    JT.openSettings = openDialog;

    JT.register({
        init() {
            JT.onDomChange(addMenuEntry);
        }
    });
})(window.JellyTwiggy);
