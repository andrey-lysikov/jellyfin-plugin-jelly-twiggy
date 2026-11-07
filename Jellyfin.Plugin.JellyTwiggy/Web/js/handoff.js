// "Continue here" and "Play on" between the user's devices, compatible with Swiftly Player.
// The server picks sessions and sends the commands; receivers build their own playback plan.
(function (JT) {
    'use strict';

    const TICKS_PER_SECOND = 1e7;
    const TARGETS_TTL_MS = 10000;

    JT.addStrings({
        en: {
            continueHere: 'Continue in browser',
            moveTo: 'Move to another device',
            sendTo: 'Send to',
            noTargets: 'No other devices with Swiftly',
            continueHereConfirm: 'Stop playback on “{device}” and continue in this browser from the same position?',
            continueAction: 'Continue',
            continuingHere: 'Continuing here from “{device}”',
            sourceGone: 'Nothing to take over: playback on that device has ended',
            noOwnSession: 'This browser can’t receive playback right now, reload the page',
            handoffFailed: 'Could not hand over playback',
            playOn: 'Play on…',
            playOnTitle: 'Play on',
            playingOn: 'Playing on “{device}”',
            deviceUnavailable: '“{device}” is unavailable',
            nowPlayingThere: 'playing now',
            settingsHandoffSection: 'Other devices',
            enableHandoff: 'Continue watching with Swiftly clients',
            enableHandoffHint: '“Continue here” in active streams and “Play on…” on item pages and in the player, between your devices with Swiftly Player.'
        },
        ru: {
            continueHere: 'Продолжить в браузере',
            moveTo: 'Перенести на другое устройство',
            sendTo: 'Отправить на',
            noTargets: 'Нет других устройств со Swiftly',
            continueHereConfirm: 'Остановить воспроизведение на «{device}» и продолжить в этом браузере с того же места?',
            continueAction: 'Продолжить',
            continuingHere: 'Продолжаем здесь с «{device}»',
            sourceGone: 'Забирать нечего: на том устройстве воспроизведение закончилось',
            noOwnSession: 'Этот браузер сейчас не может принять воспроизведение, обновите страницу',
            handoffFailed: 'Не удалось передать воспроизведение',
            playOn: 'Играть на…',
            playOnTitle: 'Играть на',
            playingOn: 'Играет на «{device}»',
            deviceUnavailable: 'Устройство «{device}» недоступно',
            nowPlayingThere: 'сейчас играет',
            settingsHandoffSection: 'Другие устройства',
            enableHandoff: 'Продолжать просмотр с клиентами Swiftly',
            enableHandoffHint: '«Продолжить здесь» в активных потоках и «Играть на…» на странице элемента и в плеере — между вашими устройствами со Swiftly Player.'
        },
        de: {
            continueHere: 'Im Browser fortsetzen',
            moveTo: 'Auf ein anderes Gerät verschieben',
            sendTo: 'Senden an',
            noTargets: 'Keine anderen Geräte mit Swiftly',
            continueHereConfirm: 'Wiedergabe auf „{device}“ stoppen und in diesem Browser an derselben Stelle fortsetzen?',
            continueAction: 'Fortsetzen',
            continuingHere: 'Hier fortgesetzt von „{device}“',
            sourceGone: 'Nichts zu übernehmen: Die Wiedergabe auf dem Gerät ist beendet',
            noOwnSession: 'Dieser Browser kann gerade keine Wiedergabe empfangen, Seite neu laden',
            handoffFailed: 'Wiedergabe konnte nicht übergeben werden',
            playOn: 'Abspielen auf…',
            playOnTitle: 'Abspielen auf',
            playingOn: 'Läuft auf „{device}“',
            deviceUnavailable: '„{device}“ ist nicht erreichbar',
            nowPlayingThere: 'spielt gerade',
            settingsHandoffSection: 'Andere Geräte',
            enableHandoff: 'Wiedergabe mit Swiftly-Clients fortsetzen',
            enableHandoffHint: '„Hier fortsetzen“ in den aktiven Streams und „Abspielen auf…“ auf Detailseiten und im Player – zwischen Ihren Geräten mit Swiftly Player.'
        },
        ar: {
            continueHere: 'المتابعة في المتصفح',
            moveTo: 'النقل إلى جهاز آخر',
            sendTo: 'إرسال إلى',
            noTargets: 'لا توجد أجهزة أخرى عليها Swiftly',
            continueHereConfirm: 'إيقاف التشغيل على «{device}» والمتابعة في هذا المتصفح من الموضع نفسه؟',
            continueAction: 'متابعة',
            continuingHere: 'تتم المتابعة هنا من «{device}»',
            sourceGone: 'لا يوجد ما يمكن نقله: انتهى التشغيل على ذلك الجهاز',
            noOwnSession: 'لا يمكن لهذا المتصفح استقبال التشغيل الآن، أعد تحميل الصفحة',
            handoffFailed: 'تعذّر نقل التشغيل',
            playOn: 'تشغيل على…',
            playOnTitle: 'تشغيل على',
            playingOn: 'يجري التشغيل على «{device}»',
            deviceUnavailable: '«{device}» غير متاح',
            nowPlayingThere: 'قيد التشغيل الآن',
            settingsHandoffSection: 'أجهزة أخرى',
            enableHandoff: 'متابعة المشاهدة مع عملاء Swiftly',
            enableHandoffHint: '«المتابعة هنا» في البث النشط و«تشغيل على…» في صفحات العناصر وفي المشغل، بين أجهزتك التي تستخدم Swiftly Player.'
        },
        be: {
            continueHere: 'Працягнуць у браўзеры',
            moveTo: 'Перанесці на іншую прыладу',
            sendTo: 'Адправіць на',
            noTargets: 'Няма іншых прылад са Swiftly',
            continueHereConfirm: 'Спыніць прайграванне на «{device}» і працягнуць у гэтым браўзеры з таго ж месца?',
            continueAction: 'Працягнуць',
            continuingHere: 'Працягваем тут з «{device}»',
            sourceGone: 'Няма чаго забіраць: на той прыладзе прайграванне скончылася',
            noOwnSession: 'Гэты браўзер зараз не можа прыняць прайграванне, абнавіце старонку',
            handoffFailed: 'Не ўдалося перадаць прайграванне',
            playOn: 'Прайграць на…',
            playOnTitle: 'Прайграць на',
            playingOn: 'Прайграецца на «{device}»',
            deviceUnavailable: 'Прылада «{device}» недаступная',
            nowPlayingThere: 'зараз прайграецца',
            settingsHandoffSection: 'Іншыя прылады',
            enableHandoff: 'Працягваць прагляд з кліентамі Swiftly',
            enableHandoffHint: '«Працягнуць тут» у актыўных патоках і «Прайграць на…» на старонцы элемента і ў прайгравальніку — паміж вашымі прыладамі са Swiftly Player.'
        },
        cs: {
            continueHere: 'Pokračovat v prohlížeči',
            moveTo: 'Přesunout na jiné zařízení',
            sendTo: 'Odeslat do',
            noTargets: 'Žádná další zařízení se Swiftly',
            continueHereConfirm: 'Zastavit přehrávání na „{device}“ a pokračovat v tomto prohlížeči od stejného místa?',
            continueAction: 'Pokračovat',
            continuingHere: 'Pokračujeme zde ze zařízení „{device}“',
            sourceGone: 'Není co převzít: přehrávání na tom zařízení skončilo',
            noOwnSession: 'Tento prohlížeč teď nemůže přijmout přehrávání, obnovte stránku',
            handoffFailed: 'Přehrávání se nepodařilo předat',
            playOn: 'Přehrát na…',
            playOnTitle: 'Přehrát na',
            playingOn: 'Přehrává se na „{device}“',
            deviceUnavailable: '„{device}“ není dostupné',
            nowPlayingThere: 'právě hraje',
            settingsHandoffSection: 'Další zařízení',
            enableHandoff: 'Pokračovat ve sledování s klienty Swiftly',
            enableHandoffHint: '„Pokračovat zde“ v aktivních streamech a „Přehrát na…“ na stránkách položek a v přehrávači – mezi vašimi zařízeními se Swiftly Player.'
        },
        fr: {
            continueHere: 'Continuer dans le navigateur',
            moveTo: 'Déplacer vers un autre appareil',
            sendTo: 'Envoyer vers',
            noTargets: 'Aucun autre appareil avec Swiftly',
            continueHereConfirm: 'Arrêter la lecture sur « {device} » et continuer dans ce navigateur au même endroit ?',
            continueAction: 'Continuer',
            continuingHere: 'Reprise ici depuis « {device} »',
            sourceGone: 'Rien à reprendre : la lecture sur cet appareil est terminée',
            noOwnSession: 'Ce navigateur ne peut pas recevoir la lecture pour le moment, rechargez la page',
            handoffFailed: 'Impossible de transférer la lecture',
            playOn: 'Jouer sur…',
            playOnTitle: 'Jouer sur',
            playingOn: 'Lecture sur « {device} »',
            deviceUnavailable: '« {device} » n’est pas disponible',
            nowPlayingThere: 'en cours de lecture',
            settingsHandoffSection: 'Autres appareils',
            enableHandoff: 'Continuer la lecture avec les clients Swiftly',
            enableHandoffHint: '« Continuer ici » dans les flux actifs et « Jouer sur… » sur les pages des médias et dans le lecteur, entre vos appareils équipés de Swiftly Player.'
        },
        it: {
            continueHere: 'Continua nel browser',
            moveTo: 'Sposta su un altro dispositivo',
            sendTo: 'Invia a',
            noTargets: 'Nessun altro dispositivo con Swiftly',
            continueHereConfirm: 'Interrompere la riproduzione su «{device}» e continuare in questo browser dallo stesso punto?',
            continueAction: 'Continua',
            continuingHere: 'Si continua qui da «{device}»',
            sourceGone: 'Niente da riprendere: la riproduzione su quel dispositivo è terminata',
            noOwnSession: 'Questo browser al momento non può ricevere la riproduzione, ricarica la pagina',
            handoffFailed: 'Impossibile trasferire la riproduzione',
            playOn: 'Riproduci su…',
            playOnTitle: 'Riproduci su',
            playingOn: 'In riproduzione su «{device}»',
            deviceUnavailable: '«{device}» non è disponibile',
            nowPlayingThere: 'in riproduzione',
            settingsHandoffSection: 'Altri dispositivi',
            enableHandoff: 'Continua a guardare con i client Swiftly',
            enableHandoffHint: '«Continua qui» negli stream attivi e «Riproduci su…» nelle pagine dei contenuti e nel lettore, tra i tuoi dispositivi con Swiftly Player.'
        },
        ja: {
            continueHere: 'ブラウザーで続きを再生',
            moveTo: '別のデバイスに移動',
            sendTo: '送信先',
            noTargets: 'Swiftly を使っている他のデバイスはありません',
            continueHereConfirm: '「{device}」での再生を停止し、このブラウザーで同じ位置から続けますか？',
            continueAction: '続ける',
            continuingHere: '「{device}」から続きを再生しています',
            sourceGone: '引き継ぐものがありません: そのデバイスでの再生は終了しました',
            noOwnSession: 'このブラウザーは現在再生を受け取れません。ページを再読み込みしてください',
            handoffFailed: '再生を引き継げませんでした',
            playOn: '再生場所…',
            playOnTitle: '再生場所',
            playingOn: '「{device}」で再生中',
            deviceUnavailable: '「{device}」は利用できません',
            nowPlayingThere: '再生中',
            settingsHandoffSection: '他のデバイス',
            enableHandoff: 'Swiftly クライアントと視聴を引き継ぐ',
            enableHandoffHint: 'アクティブなストリームの「ここで続ける」と、メディアページやプレーヤーの「再生場所…」を、Swiftly Player を使うあなたのデバイス間で利用できます。'
        },
        kk: {
            continueHere: 'Brauzerde jalğastyru',
            moveTo: 'Basqa qūrylğyğa köşıru',
            sendTo: 'Jıberu',
            noTargets: 'Swiftly bar basqa qūrylğylar joq',
            continueHereConfirm: '«{device}» qūrylğysynda oinatudy toqtatyp, osy brauzerde sol jerden jalğastyru kerek pe?',
            continueAction: 'Jalğastyru',
            continuingHere: '«{device}» qūrylğysynan osynda jalğasuda',
            sourceGone: 'Alatyn eşteñe joq: ol qūrylğyda oinatu aiaqtaldy',
            noOwnSession: 'Būl brauzer qazır oinatudy qabyldai almaidy, bettı qaita jükteñız',
            handoffFailed: 'Oinatudy beru mümkın bolmady',
            playOn: 'Qūrylğyda oinatu…',
            playOnTitle: 'Qūrylğyda oinatu',
            playingOn: '«{device}» qūrylğysynda oinatyluda',
            deviceUnavailable: '«{device}» qoljetımsız',
            nowPlayingThere: 'qazır oinatyluda',
            settingsHandoffSection: 'Basqa qūrylğylar',
            enableHandoff: 'Swiftly klientterımen qaraudy jalğastyru',
            enableHandoffHint: 'Belsendı ağyndardağy «Osynda jalğastyru» jäne element betterı men oinatqyştağy «Qūrylğyda oinatu…» — Swiftly Player ornatylğan qūrylğylaryñyz arasynda.'
        },
        pl: {
            continueHere: 'Kontynuuj w przeglądarce',
            moveTo: 'Przenieś na inne urządzenie',
            sendTo: 'Wyślij do',
            noTargets: 'Brak innych urządzeń ze Swiftly',
            continueHereConfirm: 'Zatrzymać odtwarzanie na „{device}” i kontynuować w tej przeglądarce od tego samego miejsca?',
            continueAction: 'Kontynuuj',
            continuingHere: 'Kontynuacja tutaj z „{device}”',
            sourceGone: 'Nie ma czego przejąć: odtwarzanie na tym urządzeniu się zakończyło',
            noOwnSession: 'Ta przeglądarka nie może teraz odebrać odtwarzania, odśwież stronę',
            handoffFailed: 'Nie udało się przekazać odtwarzania',
            playOn: 'Odtwarzaj na…',
            playOnTitle: 'Odtwarzaj na',
            playingOn: 'Odtwarzanie na „{device}”',
            deviceUnavailable: '„{device}” jest niedostępne',
            nowPlayingThere: 'teraz odtwarza',
            settingsHandoffSection: 'Inne urządzenia',
            enableHandoff: 'Kontynuuj oglądanie z klientami Swiftly',
            enableHandoffHint: '„Kontynuuj tutaj” w aktywnych strumieniach i „Odtwarzaj na…” na stronach elementów i w odtwarzaczu – między Twoimi urządzeniami ze Swiftly Player.'
        },
        sr: {
            continueHere: 'Настави у прегледачу',
            moveTo: 'Премести на други уређај',
            sendTo: 'Пошаљи на',
            noTargets: 'Нема других уређаја са Swiftly',
            continueHereConfirm: 'Зауставити репродукцију на „{device}“ и наставити у овом прегледачу од истог места?',
            continueAction: 'Настави',
            continuingHere: 'Наставља се овде са „{device}“',
            sourceGone: 'Нема шта да се преузме: репродукција на том уређају је завршена',
            noOwnSession: 'Овај прегледач тренутно не може да прими репродукцију, освежите страницу',
            handoffFailed: 'Није могуће пренети репродукцију',
            playOn: 'Репродукуј на…',
            playOnTitle: 'Репродукуј на',
            playingOn: 'Репродукује се на „{device}“',
            deviceUnavailable: '„{device}“ није доступан',
            nowPlayingThere: 'тренутно репродукује',
            settingsHandoffSection: 'Други уређаји',
            enableHandoff: 'Настави гледање са Swiftly клијентима',
            enableHandoffHint: '„Настави овде“ у активним стримовима и „Репродукуј на…“ на страницама ставки и у плејеру — између ваших уређаја са Swiftly Player.'
        },
        zh: {
            continueHere: '在浏览器中继续',
            moveTo: '移动到其他设备',
            sendTo: '发送到',
            noTargets: '没有其他安装了 Swiftly 的设备',
            continueHereConfirm: '停止“{device}”上的播放，并在此浏览器中从相同位置继续？',
            continueAction: '继续',
            continuingHere: '已从“{device}”接续到这里',
            sourceGone: '没有可接续的内容：该设备上的播放已结束',
            noOwnSession: '此浏览器暂时无法接收播放，请刷新页面',
            handoffFailed: '无法转移播放',
            playOn: '播放到…',
            playOnTitle: '播放到',
            playingOn: '正在“{device}”上播放',
            deviceUnavailable: '“{device}”不可用',
            nowPlayingThere: '正在播放',
            settingsHandoffSection: '其他设备',
            enableHandoff: '与 Swiftly 客户端接续观看',
            enableHandoffHint: '活动串流中的“在此继续”和媒体页面及播放器中的“播放到…”，可在你安装了 Swiftly Player 的设备之间使用。'
        }
    });

    function failureText(e, device) {
        switch (e?.status) {
            case 404: return device ? JT.t('deviceUnavailable', { device }) : JT.t('sourceGone');
            case 409: return JT.t('noOwnSession');
            default: return JT.t('handoffFailed');
        }
    }

    // ---- Continue here ---------------------------------------------------------------------

    // Called from the active streams panel with a session the server marked CanTakeOver.
    JT.takeOver = async function (session) {
        try {
            const result = await JT.post(`JellyTwiggy/Handoff/Takeover/${encodeURIComponent(session.Id)}`);
            JT.toast(JT.t('continuingHere', { device: result?.DeviceName || session.DeviceName || '' }));
        } catch (e) {
            console.error('[JellyTwiggy] takeover', e);
            JT.toast(failureText(e));
        }
    };

    // Taking over stops playback on the other device, so the user confirms it first.
    JT.confirmTakeOver = function (session) {
        const dialog = JT.dialog(JT.t('continueHere'));
        dialog.body.appendChild(JT.el('p', { text: JT.t('continueHereConfirm', { device: session.DeviceName || session.Client || '' }) }));
        dialog.footer.append(
            JT.el('button', { type: 'button', className: 'jt-button', text: JT.t('cancel'), onClick: () => dialog.close() }),
            JT.el('button', {
                type: 'button',
                className: 'jt-button jt-button-primary',
                text: JT.t('continueAction'),
                onClick: () => {
                    dialog.close();
                    JT.takeOver(session);
                }
            }));
        dialog.footer.lastElementChild.focus();
    };

    // ---- Play on ---------------------------------------------------------------------------

    // Off by default; the user turns it on in the Jelly Twiggy settings.
    const enabled = () => !!JT.settings?.EnableHandoff;

    let targets = [];
    let targetsAt = 0;
    let targetsLoading = null;

    // Targets are kept as device ids: the server resolves the current session when sending.
    function loadTargets(force) {
        if (!force && Date.now() - targetsAt < TARGETS_TTL_MS) {
            return Promise.resolve(targets);
        }
        targetsLoading ??= JT.get('JellyTwiggy/Handoff/Targets')
            .then((list) => {
                targets = list || [];
                targetsAt = Date.now();
                return targets;
            })
            .catch((e) => {
                console.error('[JellyTwiggy] handoff targets', e);
                return targets;
            })
            .finally(() => {
                targetsLoading = null;
                updateButtons();
            });
        return targetsLoading;
    }

    async function send(target, body) {
        try {
            const result = await JT.post('JellyTwiggy/Handoff/Play', { DeviceId: target.DeviceId, ...body });
            JT.toast(JT.t('playingOn', { device: result?.DeviceName || target.DeviceName || '' }));
        } catch (e) {
            console.error('[JellyTwiggy] play on', e);
            JT.toast(failureText(e, target.DeviceName || target.DeviceId));
            loadTargets(true);
        }
    }

    // A fresh list every time: the chosen device may have gone since the button appeared.
    async function pickTarget(buildBody) {
        const list = await loadTargets(true);
        if (!list.length) {
            JT.toast(JT.t('handoffFailed'));
            return;
        }

        const dialog = JT.dialog(JT.t('playOnTitle'));
        for (const target of list) {
            dialog.body.appendChild(targetRow(target, () => {
                dialog.close();
                send(target, buildBody());
            }));
        }
    }

    function targetRow(target, onPick) {
        return JT.el('button', { type: 'button', className: 'jt-handoff-target', onClick: onPick }, [
            JT.icon('tv'),
            JT.el('span', { className: 'jt-handoff-target-text' }, [
                JT.el('span', { text: target.DeviceName || target.DeviceId }),
                JT.el('span', { className: 'jt-muted', text: [target.Client, target.ItemId ? JT.t('nowPlayingThere') : null].filter(Boolean).join(' · ') })
            ])
        ]);
    }

    // ---- Move A → B (active streams panel) -------------------------------------------------

    let menu = null;

    function closeMenu() {
        menu?.remove();
        menu = null;
        document.removeEventListener('click', onMenuOutside, true);
    }

    function onMenuOutside(e) {
        if (menu && !menu.contains(e.target) && !e.target.closest('.jt-session-move')) {
            closeMenu();
        }
    }

    async function move(session, target) {
        try {
            const result = await JT.post('JellyTwiggy/Handoff/Move', { SessionId: session.Id, DeviceId: target.DeviceId });
            JT.toast(JT.t('playingOn', { device: result?.DeviceName || target.DeviceName || '' }));
        } catch (e) {
            console.error('[JellyTwiggy] move', e);
            JT.toast(failureText(e, target.DeviceName || target.DeviceId));
        }
    }

    // Drop-down under the button with the user's other Swiftly devices; the source's own is left out.
    JT.openMoveMenu = async function (session, anchor) {
        if (menu) {
            closeMenu();
            return;
        }

        const rect = anchor.getBoundingClientRect();
        const opened = menu = JT.el('div', { className: 'jt-handoff-menu' }, JT.el('div', { className: 'jt-handoff-menu-empty', text: JT.t('loading') }));
        menu.style.top = `${rect.bottom + 4}px`;
        menu.style.right = `${Math.max(8, window.innerWidth - rect.right)}px`;
        document.body.appendChild(menu);
        document.addEventListener('click', onMenuOutside, true);

        const list = (await loadTargets(true)).filter((target) => target.DeviceId !== session.DeviceId);
        if (menu !== opened) {
            return;
        }
        menu.replaceChildren(...(list.length
            ? list.map((target) => targetRow(target, () => {
                closeMenu();
                move(session, target);
            }))
            : [JT.el('div', { className: 'jt-handoff-menu-empty', text: JT.t('noTargets') })]));
    };

    // Item page: the version and tracks chosen here go along; no start position resumes a started item.
    function itemBody(page, itemId) {
        const value = (selector) => page.querySelector(selector)?.value || null;
        const index = (selector) => {
            const parsed = parseInt(value(selector), 10);
            return Number.isFinite(parsed) ? parsed : null;
        };
        return {
            ItemId: itemId,
            MediaSourceId: value('.selectSource'),
            AudioStreamIndex: index('.selectAudio'),
            SubtitleStreamIndex: index('.selectSubtitles')
        };
    }

    // Player: what plays here moves there from the current position, then stops here.
    function playerBody() {
        const time = document.querySelector('video.htmlvideoplayer')?.currentTime;
        return { FromPlayer: true, StartPositionTicks: Number.isFinite(time) ? Math.floor(time * TICKS_PER_SECOND) : null };
    }

    // Only video goes to "Play on": films, episodes, series, seasons and other videos.
    const videoItems = new Map();
    function isVideo(itemId) {
        if (!videoItems.has(itemId)) {
            videoItems.set(itemId, JT.get(`Items/${itemId}?userId=${window.ApiClient.getCurrentUserId()}`)
                .then((item) => item?.MediaType === 'Video' || item?.Type === 'Series' || item?.Type === 'Season')
                .catch(() => false));
        }
        return videoItems.get(itemId);
    }

    // jellyfin-web reuses the item page between items, so the button follows the current one.
    function placeItemButton() {
        if (!enabled()) {
            return;
        }
        const itemId = JT.route().startsWith('#/details') ? JT.routeParam('id') : null;
        const page = itemId && JT.visiblePage('.itemDetailPage');
        const play = page?.querySelector('.mainDetailButtons .btnPlay');
        if (!play) {
            return;
        }

        let button = play.parentElement.querySelector('.jt-handoff-item');
        if (!button) {
            button = JT.el('button', {
                type: 'button',
                className: 'emby-button button-flat detailButton jt-handoff-button jt-handoff-item hide',
                title: JT.t('playOn'),
                'aria-label': JT.t('playOn'),
                onClick: () => pickTarget(() => itemBody(page, button.dataset.jtItem))
            }, JT.el('div', { className: 'detailButton-content' }, JT.icon('tv', 'detailButton-icon')));
            play.insertAdjacentElement('afterend', button);
            const nativeIcon = play.querySelector('.material-icons');
            if (nativeIcon) {
                button.querySelector('.material-icons').style.fontSize = getComputedStyle(nativeIcon).fontSize;
            }
        }
        if (button.dataset.jtItem === itemId) {
            return;
        }

        button.dataset.jtItem = itemId;
        delete button.dataset.jtVideo;
        updateButtons();
        isVideo(itemId).then((video) => {
            if (video && button.dataset.jtItem === itemId) {
                button.dataset.jtVideo = 'true';
                updateButtons();
            }
        });
        loadTargets();
    }

    function placePlayerButton() {
        if (!enabled()) {
            return;
        }
        const bar = document.querySelector('#videoOsdPage:not(.hide) .videoOsdBottom');
        const settings = bar?.querySelector('.btnVideoOsdSettings');
        if (!settings || bar.querySelector('.jt-handoff-player')) {
            return;
        }

        settings.insertAdjacentElement('beforebegin', JT.el('button', {
            type: 'button',
            className: 'paper-icon-button-light autoSize jt-handoff-button jt-handoff-player hide',
            title: JT.t('playOn'),
            'aria-label': JT.t('playOn'),
            onClick: () => pickTarget(playerBody)
        }, JT.icon('tv', 'xlargePaperIconButton')));
        updateButtons();
        loadTargets();
    }

    // Buttons stay hidden while there is nowhere to send to (or the item isn't a video).
    function updateButtons() {
        document.querySelectorAll('.jt-handoff-button').forEach((button) => {
            const notVideo = button.classList.contains('jt-handoff-item') && !button.dataset.jtVideo;
            button.classList.toggle('hide', !enabled() || targets.length === 0 || notVideo);
        });
    }

    JT.register({
        init() {
            JT.onDomChange(() => {
                placeItemButton();
                placePlayerButton();
                updateButtons();
            });
            // A device opened later shows up without leaving the page.
            setInterval(() => {
                if (enabled() && !document.hidden && document.querySelector(':is(.itemDetailPage, #videoOsdPage):not(.hide) .jt-handoff-button')) {
                    loadTargets();
                }
            }, TARGETS_TTL_MS);
            JT.onNavigate(closeMenu);
            JT.onUserChange(() => {
                targets = [];
                targetsAt = 0;
                updateButtons();
            });
        }
    });
})(window.JellyTwiggy);
