using System.Collections.Frozen;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Names of channels, picks and tasks in the server's language (channels have one name for everyone).</summary>
public static class PickNames
{
    // Column order of the table below.
    private static readonly string[] Languages = ["en", "ru", "de", "ar", "be", "cs", "fr", "it", "ja", "kk", "pl", "sr", "zh"];

    private static readonly FrozenDictionary<string, string[]> Table = new Dictionary<string, string[]>
    {
        ["channelThemed"] = ["Themed", "Тематический", "Thematisch", "مختارات موضوعية", "Тэматычны", "Tematické", "Thématique", "Tematico", "テーマ別", "Taqyryptyq", "Tematyczne", "Тематски", "主题精选"],
        ["channelBest"] = ["Best", "Лучшие", "Das Beste", "الأفضل", "Лепшыя", "Nejlepší", "Le meilleur", "Il meglio", "ベスト", "Üzdıkter", "Najlepsze", "Најбоље", "最佳"],
        ["newyear"] = ["New Year and Christmas", "Новогодние фильмы", "Neujahr und Weihnachten", "أفلام رأس السنة والميلاد", "Навагоднія фільмы", "Nový rok a Vánoce", "Nouvel An et Noël", "Capodanno e Natale", "年末年始・クリスマス", "Jaña jyl filmderı", "Nowy Rok i Boże Narodzenie", "Новогодишњи филмови", "新年与圣诞"],
        ["valentine"] = ["Valentine's Day", "День святого Валентина", "Valentinstag", "عيد الحب", "Дзень святога Валянціна", "Valentýn", "Saint-Valentin", "San Valentino", "バレンタインデー", "Äulie Valentin künı", "Walentynki", "Дан заљубљених", "情人节"],
        ["defender"] = ["Defender of the Fatherland Day", "23 февраля", "Tag des Vaterlandsverteidigers", "يوم المدافع عن الوطن", "23 лютага", "Den obránců vlasti", "Jour du défenseur de la patrie", "Giornata del difensore della patria", "祖国防衛者の日", "23 aqpan", "Dzień Obrońcy Ojczyzny", "Дан бранилаца отаџбине", "祖国保卫者日"],
        ["womensday"] = ["International Women's Day", "8 марта", "Weltfrauentag", "اليوم العالمي للمرأة", "8 сакавіка", "Mezinárodní den žen", "Journée internationale des femmes", "Festa della donna", "国際女性デー", "8 nauryz", "Dzień Kobiet", "Међународни дан жена", "国际妇女节"],
        ["aprilfools"] = ["April Fools' Day: best comedies", "1 апреля: лучшие комедии", "1. April: die besten Komödien", "كذبة أبريل: أفضل الكوميديا", "1 красавіка: лепшыя камедыі", "Apríl: nejlepší komedie", "1er avril : les meilleures comédies", "Primo aprile: le migliori commedie", "エイプリルフール：傑作コメディ", "1 säuır: üzdık komediialar", "Prima aprilis: najlepsze komedie", "Први април: најбоље комедије", "愚人节：最佳喜剧"],
        ["cosmonautics"] = ["Cosmonautics Day", "День космонавтики", "Tag der Kosmonautik", "يوم رواد الفضاء", "Дзень касманаўтыкі", "Den kosmonautiky", "Journée de la cosmonautique", "Giornata della cosmonautica", "宇宙飛行士の日", "Ğaryşkerler künı", "Dzień Kosmonautyki", "Дан космонаутике", "航天节"],
        ["starwars"] = ["Star Wars", "Звёздные войны", "Star Wars", "حرب النجوم", "Зорныя войны", "Star Wars", "Star Wars", "Star Wars", "スター・ウォーズ", "Jūldyzdy soğystar", "Gwiezdne wojny", "Ратови звезда", "星球大战"],
        ["victory"] = ["Victory Day", "9 мая", "Tag des Sieges", "يوم النصر", "9 мая", "Den vítězství", "Jour de la Victoire", "Giorno della Vittoria", "戦勝記念日", "9 mamyr", "Dzień Zwycięstwa", "Дан победе", "胜利日"],
        ["summer"] = ["Summer", "Лето", "Sommer", "الصيف", "Лета", "Léto", "Été", "Estate", "夏", "Jaz", "Lato", "Лето", "夏日"],
        ["harrypotter"] = ["The Wizarding World of Harry Potter", "Волшебный мир Гарри Поттера", "Die Zauberwelt von Harry Potter", "عالم هاري بوتر السحري", "Чароўны свет Гары Потэра", "Kouzelnický svět Harryho Pottera", "Le monde des sorciers de Harry Potter", "Il mondo magico di Harry Potter", "ハリー・ポッターの魔法ワールド", "Garri Potterdıñ siqyrly älemı", "Czarodziejski świat Harry'ego Pottera", "Чаробни свет Харија Потера", "哈利·波特魔法世界"],
        ["school"] = ["Back to School", "Снова в школу", "Zurück in die Schule", "العودة إلى المدرسة", "Зноў у школу", "Zpátky do školy", "La rentrée", "Ritorno a scuola", "新学期", "Qaitadan mektepke", "Powrót do szkoły", "Повратак у школу", "开学季"],
        ["tolkien"] = ["Tolkien's Middle-earth", "Мир Толкина", "Tolkiens Mittelerde", "أرض تولكين الوسطى", "Свет Толкіна", "Tolkienova Středozem", "La Terre du Milieu de Tolkien", "La Terra di Mezzo di Tolkien", "トールキンの中つ国", "Tolkinnıñ älemı", "Śródziemie Tolkiena", "Толкинова Средња земља", "托尔金的中土世界"],
        ["bond"] = ["James Bond", "Джеймс Бонд", "James Bond", "جيمس بوند", "Джэймс Бонд", "James Bond", "James Bond", "James Bond", "ジェームズ・ボンド", "Djeims Bond", "James Bond", "Џејмс Бонд", "詹姆斯·邦德"],
        ["halloween"] = ["Halloween", "Хеллоуин", "Halloween", "الهالوين", "Хэлоўін", "Halloween", "Halloween", "Halloween", "ハロウィン", "Hellouin", "Halloween", "Ноћ вештица", "万圣节"],
        ["bttf"] = ["Back to the Future", "Назад в будущее", "Zurück in die Zukunft", "العودة إلى المستقبل", "Назад у будучыню", "Návrat do budoucnosti", "Retour vers le futur", "Ritorno al futuro", "バック・トゥ・ザ・フューチャー", "Bolaşaqqa oralu", "Powrót do przyszłości", "Повратак у будућност", "回到未来"],
        ["marvel"] = ["Marvel Universe", "Мир Marvel", "Marvel-Universum", "عالم مارفل", "Свет Marvel", "Svět Marvelu", "Univers Marvel", "Universo Marvel", "マーベル・ユニバース", "Marvel älemı", "Uniwersum Marvela", "Марвелов универзум", "漫威宇宙"],
        ["pixar"] = ["Pixar", "Pixar", "Pixar", "Pixar", "Pixar", "Pixar", "Pixar", "Pixar", "ピクサー", "Pixar", "Pixar", "Pixar", "皮克斯"],
        ["ghibli"] = ["Studio Ghibli", "Студия Гибли", "Studio Ghibli", "استوديو جيبلي", "Студыя Гіблі", "Studio Ghibli", "Studio Ghibli", "Studio Ghibli", "スタジオジブリ", "Ghibli studiiasy", "Studio Ghibli", "Студио Гибли", "吉卜力工作室"],
        ["top50"] = ["Top 50 of all time", "ТОП-50 всех времён", "Top 50 aller Zeiten", "أفضل 50 فيلمًا على الإطلاق", "ТОП-50 усіх часоў", "Top 50 všech dob", "Top 50 de tous les temps", "Top 50 di sempre", "歴代トップ50", "Barlyq uaqyttyñ TOP-50", "Top 50 wszech czasów", "Топ 50 свих времена", "影史前50"],
        ["serverfavorites"] = ["Server favorites", "Любимое на сервере", "Lieblinge des Servers", "المفضلة على الخادم", "Любімае на серверы", "Oblíbené na serveru", "Favoris du serveur", "I preferiti del server", "サーバーのお気に入り", "Serverdegı süiıktı filmder", "Ulubione na serwerze", "Омиљено на серверу", "服务器最爱"],
        ["popularweek"] = ["Popular this week", "Популярное за неделю", "Diese Woche beliebt", "الأكثر شعبية هذا الأسبوع", "Папулярнае за тыдзень", "Populární tento týden", "Populaires cette semaine", "Popolari questa settimana", "今週の人気作", "Aptanyñ tanymaldary", "Popularne w tym tygodniu", "Популарно ове недеље", "本周热门"],
        ["underrated"] = ["Underrated", "Недооценённые", "Unterschätzt", "أفلام لم تنل حقها", "Недаацэненыя", "Nedocenění", "Sous-estimés", "Sottovalutati", "過小評価された作品", "Bağalanbağandar", "Niedocenione", "Потцењени", "被低估的佳作"],
        ["books"] = ["Book adaptations", "Экранизации книг", "Buchverfilmungen", "مقتبسة من كتب", "Экранізацыі кніг", "Knižní adaptace", "Adaptations de livres", "Tratti da libri", "小説の映画化", "Kıtap jelısımen tüsırılgender", "Ekranizacje książek", "Екранизације књига", "文学改编"],
        ["hiddengems"] = ["Hidden gems", "Скрытые жемчужины", "Verborgene Perlen", "جواهر خفية", "Схаваныя жамчужыны", "Skryté klenoty", "Pépites cachées", "Gemme nascoste", "隠れた名作", "Jasyryn ınjuler", "Ukryte perełki", "Скривени бисери", "遗珠之作"],
        ["taskThemed"] = ["Jelly Twiggy: themed pick", "Jelly Twiggy: тематическая подборка", "Jelly Twiggy: Themenauswahl", "Jelly Twiggy: المختارات الموضوعية", "Jelly Twiggy: тэматычная падборка", "Jelly Twiggy: tematický výběr", "Jelly Twiggy : sélection thématique", "Jelly Twiggy: selezione a tema", "Jelly Twiggy: テーマ別セレクション", "Jelly Twiggy: taqyryptyq irıkteme", "Jelly Twiggy: wybór tematyczny", "Jelly Twiggy: тематски избор", "Jelly Twiggy：主题精选"],
        ["taskThemedHelp"] = ["Picks the films of the current holiday or film series for the «Themed» channel.", "Подбирает фильмы текущего праздника или франшизы для канала «Тематический».", "Wählt die Filme des aktuellen Feiertags oder der Filmreihe für den Kanal „Thematisch“ aus.", "يختار أفلام المناسبة الحالية أو السلسلة لقناة «مختارات موضوعية».", "Падбірае фільмы бягучага свята або франшызы для канала «Тэматычны».", "Vybírá filmy aktuálního svátku nebo filmové série pro kanál „Tematické“.", "Choisit les films de la fête ou de la saga du moment pour la chaîne « Thématique ».", "Sceglie i film della festa o della saga del momento per il canale «Tematico».", "「テーマ別」チャンネル用に、現在の祝日またはシリーズの映画を選びます。", "«Taqyryptyq» arnasy üşın ağymdağy merekenıñ nemese franşizanyñ filmderın tañdaidy.", "Wybiera filmy bieżącego święta lub serii dla kanału „Tematyczne”.", "Бира филмове тренутног празника или серијала за канал „Тематски“.", "为“主题精选”频道挑选当前节日或系列的电影。"],
        ["taskBest"] = ["Jelly Twiggy: best pick", "Jelly Twiggy: подборка «Лучшие»", "Jelly Twiggy: Auswahl „Das Beste“", "Jelly Twiggy: مختارات «الأفضل»", "Jelly Twiggy: падборка «Лепшыя»", "Jelly Twiggy: výběr „Nejlepší“", "Jelly Twiggy : sélection « Le meilleur »", "Jelly Twiggy: selezione «Il meglio»", "Jelly Twiggy: 「ベスト」セレクション", "Jelly Twiggy: «Üzdıkter» irıktemesı", "Jelly Twiggy: wybór „Najlepsze”", "Jelly Twiggy: избор „Најбоље“", "Jelly Twiggy：“最佳”精选"],
        ["taskBestHelp"] = ["Moves the «Best» channel to the next server-wide top; each run switches the top.", "Переключает канал «Лучшие» на следующий топ сервера; каждый запуск меняет топ.", "Schaltet den Kanal „Das Beste“ auf die nächste Bestenliste des Servers; jeder Lauf wechselt sie.", "ينقل قناة «الأفضل» إلى القائمة التالية على الخادم؛ كل تشغيل يغيّر القائمة.", "Пераключае канал «Лепшыя» на наступны топ сервера; кожны запуск мяняе топ.", "Přepne kanál „Nejlepší“ na další žebříček serveru; každé spuštění žebříček změní.", "Passe la chaîne « Le meilleur » au classement suivant du serveur ; chaque exécution change le classement.", "Passa il canale «Il meglio» alla classifica successiva del server; ogni esecuzione cambia classifica.", "「ベスト」チャンネルをサーバーの次のランキングに切り替えます。実行ごとに切り替わります。", "«Üzdıkter» arnasyn serverdıñ kelesı topyna auystyrady; ärbır ıske qosu topty özgertedı.", "Przełącza kanał „Najlepsze” na kolejny ranking serwera; każde uruchomienie zmienia ranking.", "Пребацује канал „Најбоље“ на следећу листу сервера; свако покретање мења листу.", "将“最佳”频道切换到服务器的下一个榜单；每次运行都会更换榜单。"]
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Gets a name in the language of a culture such as "ru" or "zh-CN"; English when there is none.</summary>
    public static string Get(string key, string? culture)
    {
        if (!Table.TryGetValue(key, out var names))
        {
            return key;
        }

        var language = (culture ?? string.Empty).Split('-')[0].ToLowerInvariant();
        var index = Array.IndexOf(Languages, language);
        return names[index < 0 ? 0 : index];
    }
}
