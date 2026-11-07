using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Which channel a pick belongs to and how it is chosen.</summary>
public enum PickKind
{
    /// <summary>A holiday or season, chosen by tags, titles, genres or country.</summary>
    Holiday,

    /// <summary>A film series or studio, in release order.</summary>
    Franchise,

    /// <summary>A server-wide top for the «Best» channel.</summary>
    Top
}

/// <summary>One occurrence of a window: its days and the holiday itself, which decides between overlapping windows.</summary>
public readonly record struct PickOccurrence(DateOnly Start, DateOnly End, DateOnly Anchor);

/// <summary>A yearly date window: the month before a holiday (<see cref="Due(int, int, int)"/>) or fixed days of a season.</summary>
public sealed record PickWindow(Func<int, IReadOnlyList<PickOccurrence>> ForYear)
{
    /// <summary>Gets a fixed window, e.g. 15 November – 14 January around 31 December.</summary>
    public static PickWindow Fixed(int startMonth, int startDay, int endMonth, int endDay, int anchorMonth, int anchorDay)
    {
        static int Key(int month, int day) => (month * 100) + day;
        var wraps = Key(endMonth, endDay) < Key(startMonth, startDay);
        var anchorNextYear = Key(anchorMonth, anchorDay) < Key(startMonth, startDay);
        return new PickWindow(year =>
        [
            new PickOccurrence(
                new DateOnly(year, startMonth, startDay),
                new DateOnly(wraps ? year + 1 : year, endMonth, endDay),
                new DateOnly(anchorNextYear ? year + 1 : year, anchorMonth, anchorDay))
        ]);
    }

    /// <summary>Gets the month before a holiday up to its last day: the holiday itself or, for longer ones, a few days after.</summary>
    public static PickWindow Due(int month, int day, int through = 0) => Due(year => [new DateOnly(year, month, day)], through);

    /// <summary>Gets the month before a holiday that moves (none, one or several dates a year) up to its last day.</summary>
    public static PickWindow Due(Func<int, IEnumerable<DateOnly>> dates, int through = 0) => Around(dates, -PickCalendar.LeadDays, through);

    /// <summary>Gets days around the dates a holiday falls on in a year (none, one or several, like Friday the 13th).</summary>
    public static PickWindow Around(Func<int, IEnumerable<DateOnly>> dates, int startOffset, int endOffset) =>
        new(year => dates(year).Select(day => new PickOccurrence(day.AddDays(startOffset), day.AddDays(endOffset), day)).ToList());
}

/// <summary>How a holiday matches a film: TMDb keywords, title, overview, genre and country of production.</summary>
// Countries: with CountriesOnly only films made entirely there, else a film from one of them scores like a strong tag.
// RequireGenre makes one of Genres mandatory.
public sealed record ThemeRule(
    IReadOnlySet<string> StrongTags,
    IReadOnlySet<string> WeakTags,
    Regex? Title,
    Regex? Overview,
    IReadOnlySet<string> Genres,
    bool RequireStrong,
    IReadOnlySet<string> Countries,
    bool RequireGenre,
    int Threshold,
    bool CountriesOnly);

/// <summary>Films of a franchise: TMDb collections, studios or exact original titles.</summary>
public sealed record FranchiseRule(IReadOnlySet<string> Collections, IReadOnlySet<string> Studios, IReadOnlySet<string> OriginalTitles);

/// <summary>One pick: a theme of the «Themed» channel or a top of the «Best» channel.</summary>
// Religious holidays give way to others on the same day; a season shows only when no holiday is due.
public sealed record PickDefinition(
    string Id,
    PickKind Kind,
    PickWindow? Window = null,
    ThemeRule? Rule = null,
    FranchiseRule? Franchise = null,
    int Limit = 20,
    int Minimum = 6,
    bool Religious = false,
    bool Season = false);

/// <summary>All picks the plugin knows, in display and rotation order.</summary>
public static class PickCatalog
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Genre and country names in the metadata languages seen on servers (Kinopoisk writes Russian ones).
    private static readonly string[] HorrorGenres = ["ужасы", "horror"];
    private static readonly string[] WarGenres = ["военный", "war", "kriegsfilm", "krieg"];
    private static readonly string[] RomanceGenres = ["мелодрама", "romance", "liebesfilm"];
    private static readonly string[] ComedyGenres = ["комедия", "comedy", "komödie"];
    private static readonly string[] KidsGenres = ["семейный", "детский", "мультфильм", "family", "kids", "animation", "familie"];
    private static readonly string[] FairyGenres = ["мультфильм", "семейный", "детский", "фэнтези", "animation", "family", "fantasy"];
    private static readonly string[] Russia = ["россия", "russia", "russian federation"];
    private static readonly string[] Ussr = ["ссср", "soviet union", "ussr"];
    private static readonly string[] Chinese = ["китай", "china", "гонконг", "hong kong", "тайвань", "taiwan"];
    private static readonly string[] Irish = ["ирландия", "ireland"];
    private static readonly string[] MusicGenres = ["мюзикл", "музыка", "musical", "music", "musik"];

    private static readonly string[] FaithStrong =
        ["jesus christ", "bible", "christianity", "easter", "resurrection", "crucifixion", "apostle", "biblical epic", "easter bunny",
         "old testament", "new testament", "moses", "nativity", "baptism"];

    private static readonly string[] FaithWeak = ["religion", "faith", "church", "priest", "monk", "nun", "god", "angel", "miracle", "prayer"];

    /// <summary>Gets the themes of the «Themed» channel: holidays in calendar order, then franchises.</summary>
    public static IReadOnlyList<PickDefinition> Themes { get; } =
    [
        // Through the New Year holidays of 1–8 January, Orthodox Christmas included.
        Holiday("newyear", PickWindow.Due(12, 31, through: 8),
            ["christmas", "new year's eve", "new year", "santa claus", "christmas eve", "christmas party", "christmas romance", "saving christmas",
             "christmas tree", "christmas spirit", "christmas story", "christmas day", "christmas carol", "christmas carols", "christmas movie",
             "north pole", "reindeer", "elf", "holiday season", "gingerbread"],
            ["holiday", "holidays", "winter", "snow", "gift", "holiday get-together", "snowman", "family reunion"],
            @"рождеств|новогод|нов(ый|ого|ому|ым|ом) год|(?<!\p{L})[её]лк|санта|гринч|щелкунчик|christmas|xmas|santa|grinch|nutcracker|weihnacht|silvester",
            @"рождеств|новогод|нов(ый|ого|ому|ым|ом) год|сочельник|санта|дед(а|ом|у)? мороз|christmas|santa claus|weihnacht|heiligabend|new year's eve"),
        Holiday("epiphany", PickWindow.Due(1, 19), FaithStrong, FaithWeak,
            @"крещени|библ|христ|иисус|bible|christ|jesus",
            @"крещени|иисус|христ|библи",
            requireStrong: true,
            religious: true),
        Holiday("valentine", PickWindow.Due(2, 14),
            ["valentine's day", "romcom", "romantic comedy", "love at first sight", "falling in love", "true love", "love story", "first love", "soulmates", "wedding"],
            ["romance", "love", "lovers", "love letter", "dating", "new love", "love affair", "kiss", "marriage proposal"],
            @"валентин|любов|love|valentin|liebe",
            @"день (святого )?валентина|valentine|влюбл",
            RomanceGenres),
        Holiday("defender", PickWindow.Due(2, 23),
            ["military", "army", "soldier", "soldiers", "special forces", "marine corps", "navy seal", "military operation", "war hero",
             "fighter pilot", "commando", "sniper", "military officer", "navy", "military academy", "military life", "world war ii", "world war i"],
            ["war", "combat", "battlefield", "tank", "action hero", "hero", "brotherhood", "rescue mission", "mercenary", "pilot"],
            @"солдат|арми|десант|спецназ|soldier|army|top gun",
            @"арми|солдат|спецназ|военн|десант|фронт",
            WarGenres,
            requireStrong: true),
        // Seeing winter off: Russian and Soviet fairy tales and cartoons; the week before Great Lent.
        Holiday("chinesenewyear", PickWindow.Due(PickCalendar.ChineseNewYear),
            ["chinese new year", "china", "hong kong", "wuxia", "chinese", "beijing", "shanghai", "shaolin", "dragon dance", "lunar new year",
             "ancient china", "chinese culture"],
            ["asia", "asian", "dragon", "temple", "kung fu", "martial arts"],
            @"китай|кунг-фу|шаолин|china|kung fu|shaolin",
            @"китай|кунг-фу|гонконг|пекин|шанха",
            countries: Chinese,
            countriesOnly: false,
            requireStrong: true),
        Holiday("maslenitsa", PickWindow.Due(year => [PickCalendar.OrthodoxEaster(year).AddDays(-49)]),
            ["fairy tale", "folklore", "russian folklore", "slavic mythology", "winter", "snow", "based on fairy tale", "tale"],
            ["magic", "dragon", "village", "animation", "cartoon", "musical"],
            null,
            @"сказк|богатыр|змей горыныч|баба яга|зим",
            FairyGenres,
            countries: [.. Russia, .. Ussr],
            threshold: 3,
            religious: true),
        Holiday("womensday", PickWindow.Due(3, 8),
            ["female protagonist", "woman director", "strong woman", "sisterhood", "female friendship", "women's rights", "feminism",
             "independent woman", "female empowerment"],
            ["romcom", "romance", "fashion", "wedding", "mother daughter relationship"],
            @"женщин|девушк|леди|woman|women|girl|lady|frau|mädchen",
            null,
            requireStrong: true),
        Holiday("stpatrick", PickWindow.Due(3, 17),
            ["ireland", "irish", "dublin", "st. patrick's day", "leprechaun", "irish american", "belfast", "northern ireland"],
            ["pub", "whiskey", "folk music"],
            @"ирланд|ireland|irish|dublin",
            @"ирланд|дублин",
            countries: Irish,
            countriesOnly: false,
            requireStrong: true),
        Holiday("aprilfools", PickWindow.Due(4, 1),
            ["hilarious", "slapstick comedy", "parody", "spoof", "prank", "satire", "absurd", "comedy of errors", "farce", "mockumentary"],
            ["amused", "cheerful", "lighthearted", "comedy", "dark comedy", "buddy comedy"],
            null,
            null,
            ComedyGenres),
        Holiday("easter", PickWindow.Due(year => [PickCalendar.OrthodoxEaster(year)], through: 1), FaithStrong, FaithWeak,
            @"пасх|воскрес|иисус|христ|библ|easter|jesus|christ|bible|passion|ostern",
            @"пасх|иисус|христ|библи|воскресени",
            requireStrong: true,
            religious: true),
        Holiday("cosmonautics", PickWindow.Due(4, 12),
            ["space", "space travel", "astronaut", "nasa", "spacecraft", "space mission", "outer space", "space station", "moon", "mars",
             "cosmonaut", "space exploration", "rocket", "space program", "spaceship", "moon landing", "space race", "international space station"],
            ["alien planet", "planet", "alien", "galaxy", "space colony", "satellite"],
            @"космос|космич|космонавт|(?<!\p{L})луна|(?<!\p{L})марс|гагарин|space|(?<!\p{L})moon|(?<!\p{L})mars(?!\p{L})|apollo|weltraum",
            @"космос|космонавт|астронавт|космическ|орбит|nasa|наса"),
        Holiday("earthday", PickWindow.Due(4, 22),
            ["nature", "environmental", "ecology", "climate change", "global warming", "planet earth", "environmentalism", "deforestation",
             "pollution", "wildlife", "nature documentary", "endangered species", "rainforest", "attachment to nature"],
            ["animals", "forest", "jungle", "ocean", "island"],
            @"(?<!\p{L})земл|природ|планет|earth|planet|nature",
            @"природ|эколог|планет|климат",
            requireStrong: true),
        Holiday("mayday", PickWindow.Due(5, 1),
            ["worker", "working class", "factory", "labor union", "strike", "trade union", "factory worker", "blue collar", "coal mine",
             "workplace", "office", "coworker", "boss", "workaholic", "job", "career", "office romance", "spring"],
            ["employee", "employer", "business", "work", "unemployment"],
            @"служебн|завод|бригад|трудов|(?<!\p{L})весн|spring|office",
            @"завод|на работ|сослужив|начальни|офис|(?<!\p{L})весн",
            requireStrong: true),
        Franchise("starwars", PickWindow.Due(5, 4), collections: ["10"], titles: ["Rogue One", "Rogue One: A Star Wars Story", "Solo: A Star Wars Story"]),
        Holiday("victory", PickWindow.Due(5, 9),
            ["world war ii", "nazi", "eastern front", "red army", "battle of stalingrad", "soviet union", "soviet army", "great patriotic war",
             "battle of berlin", "wehrmacht", "siege of leningrad", "partisan"],
            ["war", "soldier", "army", "military", "tank"],
            @"войн|сталинград|брест|блокад|ржев|т-34|панфилов",
            @"великой отечественной|(?<!\d)194[1-5](?!\d)|фашист|нацист|сталинград|блокад",
            WarGenres,
            requireStrong: true),
        // The best children's and family films: the genre is what counts, tags only lift a film.
        Holiday("childrensday", PickWindow.Due(6, 1),
            ["children", "kids", "child hero", "childhood", "talking animal"],
            ["friendship", "family", "adventure", "school"],
            null,
            null,
            KidsGenres,
            requireGenre: true,
            threshold: 3),
        Holiday("russiaday", PickWindow.Due(6, 12), [], [], null, null, countries: Russia, threshold: 0),
        Holiday("music", PickWindow.Due(6, 21),
            ["musical", "music", "singer", "band", "rock and roll", "concert", "musician", "rock band", "jazz", "opera", "singing", "dance",
             "dancer", "songwriter", "music band", "hip-hop", "rock star", "pop star", "composer", "piano"],
            ["song", "guitar", "dancing", "talent show", "performance"],
            @"мюзикл|музык|песн|music|song|sing|rock",
            @"музыкант|певиц|певец|концерт|мюзикл",
            MusicGenres),
        // A season, not a holiday: it fills summer days with no holiday due within a month.
        Holiday("summer", PickWindow.Fixed(6, 1, 8, 31, 7, 15),
            ["summer", "summer vacation", "beach", "summer camp", "road trip", "vacation", "surfing", "island", "beach house"],
            ["swimming", "swimming pool", "sea", "ocean", "travel", "sailing"],
            @"(?<!\p{L})лет[оа](?!\p{L})|пляж|каникул|summer|beach|sommer|urlaub",
            @"летн|(?<!\p{L})лето|каникул|пляж|отпуск",
            season: true),
        Holiday("friendship", PickWindow.Due(7, 30),
            ["friendship", "best friend", "unlikely friendship", "male friendship", "female friendship", "childhood friends", "buddy", "friends",
             "friendship between children", "teamwork"],
            ["loyalty", "road trip", "buddy comedy", "reunion"],
            @"друз|дружб|friend",
            @"(?<!\p{L})друз|дружб",
            requireStrong: true),
        Franchise("harrypotter", PickWindow.Due(7, 31, through: 7), collections: ["1241", "435259"]),
        Holiday("cinemaday", PickWindow.Due(8, 27),
            ["filmmaking", "movie business", "hollywood", "film director", "film production", "making of", "behind the scenes",
             "film within a film", "movie star", "actor", "actress", "screenwriter", "film set", "cinema", "silent film", "film industry",
             "stuntman", "movie theater"],
            ["fame", "celebrity", "los angeles, california"],
            @"(?<!\p{L})кино|голливуд|hollywood|movie|cinema",
            @"съ[её]мк|режисс[её]р|голливуд|киностуди|акт[её]р",
            requireStrong: true),
        Holiday("school", PickWindow.Due(9, 1),
            ["high school", "school", "boarding school", "teacher", "student", "school life", "school of witchcraft", "school friend", "college",
             "university", "prom", "classroom", "schoolteacher", "first day of school"],
            ["teenager", "coming of age", "teenage romance", "friendship"],
            @"школ|учител|выпускн|school|teacher|schule|lehrer",
            @"школ|учител|ученик|студент|университет|колледж|выпускн"),
        Franchise("tolkien", PickWindow.Due(9, 22), collections: ["119", "121938"]),
        Holiday("animals", PickWindow.Due(10, 4),
            ["dog", "cat", "talking animal", "animal", "pet", "horse", "animals", "anthropomorphism", "wildlife", "zoo", "puppy", "kitten", "bear",
             "penguin", "lion", "dolphin", "elephant", "talking dog", "talking cat", "animal protagonist"],
            ["nature", "forest", "jungle", "farm", "animal rescue"],
            @"собак|(?<!\p{L})кот|(?<!\p{L})п[её]с|звер|животн|(?<!\p{L})dog|(?<!\p{L})cat(?!\p{L})|animal",
            @"собак|(?<!\p{L})кот|животн|звер",
            requireStrong: true),
        Holiday("halloween", PickWindow.Due(10, 31),
            ["halloween", "haunted house", "trick or treating", "jack-o'-lantern", "ghost story", "haunting", "slasher", "vampire", "werewolf",
             "zombie", "possession", "exorcism", "poltergeist", "pumpkin", "haunted", "horror"],
            ["ghost", "witch", "monster", "supernatural", "curse", "skeleton", "black magic", "witchcraft", "graveyard", "demon", "dark fantasy"],
            @"хэллоуин|хеллоуин|halloween|призрак|вампир|зомби|ведьм|привидени|ghost|vampire|zombie|gespenst",
            @"хэллоуин|хеллоуин|halloween|нечист|привидени|призрак|вампир|зомби",
            HorrorGenres,
            requireStrong: true),
        // National Unity Day: Soviet classics, as libraries rarely tag films about history or unity.
        Holiday("unityday", PickWindow.Due(11, 4), [], [], null, null, countries: Ussr, threshold: 0),
        Holiday("thanksgiving", PickWindow.Due(year => [PickCalendar.Thanksgiving(year)]),
            ["thanksgiving", "family dinner", "turkey dinner", "gratitude", "thanksgiving dinner"],
            ["family", "holiday", "family relationships", "dysfunctional family", "family reunion"],
            @"благодарени|thanksgiving",
            @"благодарени|thanksgiving",
            requireStrong: true),
        Holiday("friday13", PickWindow.Due(PickCalendar.FridaysThe13th),
            ["friday the 13th", "slasher", "haunted house", "ghost story", "haunting", "possession", "exorcism", "poltergeist", "horror",
             "serial killer", "zombie", "vampire"],
            ["ghost", "witch", "monster", "supernatural", "skeleton", "black magic", "demon", "killer", "werewolf", "curse"],
            @"пятниц[аы] 13|friday the 13th|(?<!\p{L})ужас|кошмар|horror|nightmare",
            null,
            HorrorGenres,
            requireStrong: true),
        // No dates of their own: James Bond Day and Back to the Future Day would take October from Halloween.
        Franchise("bond", collections: ["645"]),
        Franchise("bttf", collections: ["264"]),
        Franchise("marvel", studios: ["Marvel Studios"]),
        Franchise("pixar", studios: ["Pixar", "Pixar Animation Studios"]),
        Franchise("ghibli", studios: ["Studio Ghibli"])
    ];

    /// <summary>Gets the tops of the «Best» channel in rotation order.</summary>
    public static IReadOnlyList<PickDefinition> Tops { get; } =
    [
        new("top50", PickKind.Top, Limit: 50),
        new("serverfavorites", PickKind.Top),
        new("director", PickKind.Top, Minimum: 5),
        new("popularweek", PickKind.Top),
        new("underrated", PickKind.Top),
        new("star", PickKind.Top),
        new("books", PickKind.Top),
        new("recent", PickKind.Top),
        new("hiddengems", PickKind.Top)
    ];

    /// <summary>Gets every pick, themes first.</summary>
    public static IEnumerable<PickDefinition> All => Themes.Concat(Tops);

    private static PickDefinition Holiday(
        string id,
        PickWindow window,
        string[] strong,
        string[] weak,
        string? title,
        string? overview,
        string[]? genres = null,
        bool requireStrong = false,
        string[]? countries = null,
        bool requireGenre = false,
        int threshold = PickSelector.RelevanceThreshold,
        bool countriesOnly = true,
        bool religious = false,
        bool season = false)
    {
        var rule = new ThemeRule(
            Set(strong),
            Set(weak),
            title is null ? null : new Regex(title, Options),
            overview is null ? null : new Regex(overview, Options),
            Set(genres ?? []),
            requireStrong,
            Set(countries ?? []),
            requireGenre,
            threshold,
            countriesOnly);
        return new PickDefinition(id, PickKind.Holiday, window, rule, Religious: religious, Season: season);
    }

    // Franchises are marathons: every film counts, so two are enough and up to 30 are shown.
    private static PickDefinition Franchise(string id, PickWindow? window = null, string[]? collections = null, string[]? studios = null, string[]? titles = null)
        => new(id, PickKind.Franchise, window, Franchise: new FranchiseRule(Set(collections ?? []), Set(studios ?? []), Set(titles ?? [])), Limit: 30, Minimum: 2);

    private static HashSet<string> Set(IEnumerable<string> values) => new(values, StringComparer.OrdinalIgnoreCase);
}
