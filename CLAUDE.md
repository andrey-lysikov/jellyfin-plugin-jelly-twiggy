# Jelly Twiggy — плагин для Jellyfin

Серверный плагин Jellyfin (C#/.NET), который расширяет веб-клиент: внедряет собственный JS/CSS
через плагин **File Transformation** (уже установлен на сервере) и предоставляет свои REST-эндпоинты.
Часть логики заимствуется из https://github.com/n00bcodr/Jellyfin-Enhanced (далее JE).

## Исходное ТЗ

1. **Бейджи на постерах**
   - слева вверху — разрешение + HDR/DV + кодек: `4K HDR (HEVC)`, `1080p (x264)`, `4K DV (AV1)`;
   - под ним — первая аудиодорожка: `DTS 5.1`, `AC3 2.0`;
   - справа внизу — рейтинг (по аналогии с JE).
2. **Настройки в профиле пользователя** (пользователь меняет сам):
   - Auto-skip Intro, Auto-skip Outro;
   - Auto-pause on tab switch, Auto-resume on tab switch, Auto Picture-in-Picture on tab switch,
     Long press/hold for 2x speed.
3. **Кнопка активных потоков** со списком сессий (примерно как в JE).
4. **Панель управления** (только для админа):
   - скрытие/отображение блоков;
   - перемещение блоков;
   - блок «Устройства» в том же стиле, что и список из п.3;
   - статистика по пользователям: кто что воспроизводил за 24 часа / 7 дней / месяц.
5. **«Просмотрено» на странице медиа** (фильмы, сериалы, другое видео, аудио): кружки с аватарами
   пользователей, смотревших элемент; при наведении — «Смотрел N раз, последний раз <дата>».

## План

- **Фаза 0. Каркас:** проект плагина (`IPlugin`, `IHasWebPages`), страница настроек админа,
  регистрация трансформации `index.html` через File Transformation, раздача JS/CSS как embedded
  resources, REST-контроллеры, сборка `.dll` + `meta.json`.
- **Фаза 1.** Бейджи на постерах: пакетный эндпоинт с кэшем вместо запроса на каждый постер.
- **Фаза 2.** Раздел настроек пользователя + реализация поведения плеера в JS.
- **Фаза 3.** Кнопка активных потоков в шапке (`/Sessions`, WebSocket/polling).
- **Фаза 4.** Переработка панели управления: скрытие/drag-n-drop блоков с сохранением раскладки,
  карточки устройств, блок статистики.
- **Фаза 5.** Блок «Просмотрено» с аватарами и подсказками.
- **Фаза 6.** Тестирование на сервере, упаковка, README.

## Code style

- Comments: 2 lines max, everywhere. Short, to the point, in English.
- C# docs: one-line `/// <summary>…</summary>` or `/// <inheritdoc />`; no `<param>` / `<returns>`.
- README: short, in the style of [System-Spinner](https://github.com/andrey-lysikov/System-Spinner).
- Changelog: every user-visible change goes to `Changelog.md` under `## <next version>` as a short
  bullet. Release notes and the manifest changelog are taken from that section.

## Структура

- `Jellyfin.Plugin.JellyTwiggy/` — проект плагина:
  - `Plugin.cs` — точка входа (`BasePlugin`, `IHasWebPages`), GUID `71c422cd-3468-4d47-aad3-19d3d0c5be48`;
  - `PluginServiceRegistrator.cs` — регистрация сервисов в DI;
  - `WebInjection/` — регистрация в File Transformation и вставка `<script>` в `index.html`;
  - `Api/JellyTwiggyController.cs` — весь REST под `/JellyTwiggy/...`: `client/{path}` (раздача
    `Web/**` как embedded resources, анонимно), `Status`, `ClientConfig`, `UserSettings`,
    `DashboardLayout`, `Badges`, `Items/{id}/Watchers`, `Sessions`, `Statistics`; плюс
    `JellyTwiggyDashboardController` (`LibraryResolutions`, `SystemMetrics`) и
    `JellyTwiggyHandoffController` (`Handoff/Sources|Targets|Explain|Takeover/{id}|Play`).
    Имена методов — с префиксом `JellyTwiggy`: Jellyfin делает из них operationId в swagger;
  - `History/` — журнал воспроизведений (`PlaybackTracker` слушает события сессий и
    `UserDataSaved`, `PlaybackHistoryStore` — JSONL в памяти), «Просмотрено», статистика;
  - `Media/` — бейджи (качество/звук/рейтинг), список сессий;
  - `Handoff/` — «Продолжить здесь» / «Играть на…», совместимо со Swiftly (правила и строки протокола —
    см. «Handoff со Swiftly» ниже): `HandoffRules` (чистые правила), `HandoffService` (порядок шагов),
    `IHandoffSessions` (шов над `ISessionManager` для тестов);
  - `Storage/` — пользовательские настройки и раскладка панели (JSON на пользователя);
  - `Configuration/` — конфиг плагина и страница настроек админа;
  - `Web/` — клиент: `js/jelly-twiggy.js` (загрузчик) грузит по порядку `i18n`, `core`, `badges`,
    `player`, `settings`, `streams`, `watchers`, `dashboard`, `favorites`, `mediainfo`, `sync`, `handoff`;
    стили — `css/jelly-twiggy.css` + `css/dashboard.css`; строки модулей — `JT.addStrings`.
    Глобальный объект `window.JellyTwiggy` (`JT`), модули регистрируются через `JT.register`.
- Данные плагина: `<jellyfin data>/jellytwiggy/` (не в папке плагина — переживают обновления).
- API Jellyfin отдаёт JSON в PascalCase — клиент читает поля в PascalCase.
- Панель управления (React): виджеты не перемещаем в DOM, а помечаем `data-jt-block` и
  раскладываем сгенерированным CSS (grid `order`/`display`). Свои блоки добавляем в тот же
  grid-контейнер.
- Синтаксис JS локально можно проверить через JavaScriptCore:
  `/System/Library/Frameworks/JavaScriptCore.framework/Versions/Current/Helpers/jsc -e "checkSyntax('file.js')"`.
- `manifest.json` — репозиторий плагинов Jellyfin, лежит в отдельной ветке `manifest` (в `main` его нет);
  обновляется только release-workflow, `main` бот не трогает.
- `Jellyfin.Plugin.JellyTwiggy.Tests/` — xUnit-тесты (пока только handoff), гоняются в CI.
- `.github/workflows/build.yml` — сборка, `dotnet test`, `node --check` JS на push/PR;
  `release.yml` — по тегу `vX.Y.Z.W`: сборка, zip с dll, GitHub release, обновление manifest в ветке `manifest`.

## Ограничения и заметки

- Внедрённый JS работает только в веб-клиенте и webview-обёртках; нативные клиенты
  (Android TV, Swiftfin, Infuse, Kodi) изменения не увидят.
- Jellyfin хранит лишь `PlayCount` / `LastPlayedDate` на пару пользователь–элемент; полной
  истории воспроизведений нет — для статистики нужен Playback Reporting или собственный журнал.
- JE распространяется под GPL-3.0: прямое копирование кода обязывает выпускать плагин под GPL
  (см. «Лицензия» ниже).

## Принятые решения

- **Платформа:** только Jellyfin **12.x**, .NET 10 (`net10.0`), пакеты `Jellyfin.Controller` /
  `Jellyfin.Model` закреплены на 12.0.0 (грузится на любом 12.x), `targetAbi` = `12.0.0.0`.
  C#-плагин + основной UI на JS/CSS. Панель управления — React/MUI, встраиваемся с учётом этого.
- **Встраивание JS:** через плагин **File Transformation** (обязательная зависимость) —
  регистрация трансформации `index.html` через рефлексию. `JObject` для payload создаётся через
  тип параметра `RegisterTransformation` самого FT (плагины живут в разных AssemblyLoadContext,
  своя копия Newtonsoft дала бы несовместимый тип). Свой Newtonsoft.Json не подключаем.
- **Клиенты:** только веб-клиент и webview-обёртки — это приемлемо.
- **JE** на сервере удалён, сосуществование не требуется.
- **Рейтинг:** только `CommunityRating` (плагин Kinopoisk пишет туда же — отдельно не обрабатываем).
- **История воспроизведений:** собственный журнал (подписка на события воспроизведения сервера),
  хранится бессрочно; каждая сессия — запись: пользователь, элемент, начало, фактическая
  длительность, достигнутая позиция, устройство, direct play / транскодирование.
- **«Просмотр»** — сессия, досмотренная до штатного порога Jellyfin «Макс. доля для возобновления»
  (`ServerConfiguration.MaxResumePct`, своей настройки у плагина нет) при реально просмотренной
  половине времени до этой точки (перемотка в конец не считается); повтор в течение 10 минут не
  засчитывается. Либо ручная
  отметка «просмотрено» (без учёта во времени просмотра).
- **«Смотрел N раз» (гибрид):** если по паре пользователь–элемент в журнале есть записи — N и дата
  берутся из журнала; если записей нет совсем — из `PlayCount` / `LastPlayedDate`.
  Недосмотренное: «Остановился на 45%»; сериал: «12 из 20 эпизодов, последний раз <дата>».
- **Статистика в панели:** топ пользователей по времени просмотра, топ фильмов/сериалов,
  лента последних воспроизведений, доля direct play / транскодирования; периоды 24ч / 7д / месяц.
- **Сериалы/сезоны:** агрегация по эпизодам — «12 из 20 эпизодов».
- **Видимость:** «Просмотрено» видят все, но только по элементам, доступным самому пользователю
  (отключённые пользователи не показываются; «скрыт на экране входа» — показывается). Активные потоки: админ видит все сессии,
  остальные — только свои (как в самом Jellyfin; решение из-за родительского контроля).
- **Хранение:** пользовательские настройки и раскладка панели — на сервере.
- **Значения по умолчанию:** все пользовательские опции из п.2 выключены.
- **Сегменты интро/титров:** Intro Skipper (через Media Segments API).
- **Название:** Jelly Twiggy.
- **Репозиторий:** https://github.com/andrey-lysikov/jellyfin-plugin-jelly-twiggy — код, релизы и
  `manifest.json` в ветке `manifest`; в Jellyfin добавляется
  `https://raw.githubusercontent.com/andrey-lysikov/jellyfin-plugin-jelly-twiggy/manifest/manifest.json`.
- **Сборка — только через GitHub Actions** (релиз по тегу). На локальной машине **не собираем и
  не устанавливаем** dotnet или другие тулчейны; проверка сборки — через CI.
- **Версионирование:** 4 компонента везде (сборка, `manifest.json`, тег) — текущая **0.2.6.0**,
  тег релиза `vX.Y.Z.W`.
- **Лицензия:** GPL-3.0. Код JE (тоже GPL-3.0) заимствовать можно — с указанием источника
  в комментарии у заимствованного фрагмента и в разделе Credits README.
- **Git:** пушит только пользователь. Не делать `git push`; когда для проверки нужен CI —
  остановиться и попросить запушить.
- **Локализация UI:** ru / en / de.
- **README.md** держать актуальным при изменении функций, требований или процесса установки.

## Handoff со Swiftly («Продолжить здесь» / «Играть на…»)

Совместимость со Swiftly Player: те же правила отбора, сообщения и порядок шагов. Своего протокола
между устройствами нет — только API сессий Jellyfin. Включается пользовательской настройкой
`EnableHandoff` (по умолчанию выкл.), работает только с клиентами Swiftly.

- **Строки протокола не менять** (иначе старые Swiftly перестанут понимать плагин): заголовок
  подсказки `Swiftly-Uebernahme`; Swiftly — `Client` начинается с `"Swiftly"` и ≠ `"Swiftly Music"`.
- **Не перекодировать:** отправляем только ItemIds, позицию, MediaSourceId и индексы дорожек; план
  строит принимающее устройство. Никаких `MaxStreamingBitrate`, профилей и контейнеров.
- **Только свой пользователь**, админ тоже: чужое воспроизведение не предлагать и не останавливать.
- **Не играть молча в другом месте:** цель пропала — сообщить и ничего не запускать.
- **Отбор для «Продолжить здесь»** (порядок как в Swiftly `Takeover.fits`): `DeviceId` ≠ свой →
  `UserId` = свой → есть `NowPlayingItem.Id` → видео (не `Audio`/`AudioBook`/`MusicAlbum`/`Playlist`,
  не `Swiftly Music`) → `SupportsRemoteControl` → `now − LastActivityDate ≤ 90 с`; плюс у нас — Swiftly.
  Сортировка — самая свежая первой; несколько подходящих — список, не угадывать.
- **Порядок передачи:** подсказка `{"Header":"Swiftly-Uebernahme","Text":"<имя забирающего>","TimeoutMs":1000}`
  (только Swiftly; окно 3 с, стоп — после неё и внутри окна) → `Stop` → позиция из `PlaybackStopped`
  (ждать ≤ 3 с) → `PlayNow` здесь.
- **Позиция (`Takeover.startPoint`):** нет отчёта или он равен «до» — позиция сессии (отстаёт до 10 с);
  иначе отчёт, если он ≥ позиции сессии − 1 с, иначе позиция сессии (Jellyfin обнуляет у начала/конца).
- **Цели «Играть на…»:** свой пользователь, чужое устройство, `SupportsRemoteControl`, `PlayableMediaTypes`
  содержит `Video`, свежая, Swiftly; одна запись на `DeviceId` (самая свежая); играть не обязана.
  Цель хранится как `DeviceId` (id сессии меняется при перезапуске) и разрешается в сессию при отправке.
- **Из плеера:** `PlayNow` с текущей позиции → дождаться успеха → только потом стоп у себя; подсказка не нужна.
- **Перенос A → B** (не реализован): подсказка на A → стоп A → `PlaybackStopped` ≤ 3 с → `PlayNow` на B
  с дорожками A; B не ответил — ошибка, A молча не перезапускать.
- **Состояние Swiftly:** takeover работает с 1.0.9; приём `Play` заявлен, но выпущенные версии его пока
  молча отбрасывают — отправку на Swiftly проверить нельзя, только отбор целей.
- **Отладка:** `GET /JellyTwiggy/Handoff/Explain` — причина отказа одним словом на каждое условие.
- Тестовые данные — только на de / en / ru.
