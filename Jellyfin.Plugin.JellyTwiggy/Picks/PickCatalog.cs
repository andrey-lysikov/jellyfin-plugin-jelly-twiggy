using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Which channel a pick belongs to and how it is chosen.</summary>
public enum PickKind
{
    /// <summary>A holiday or season, chosen by tags, titles and genres.</summary>
    Holiday,

    /// <summary>A film series or studio, in release order.</summary>
    Franchise,

    /// <summary>A server-wide top for the «Best» channel.</summary>
    Top
}

/// <summary>A yearly date window; it may wrap over New Year. The anchor decides between overlapping windows.</summary>
public sealed record PickWindow(int StartMonth, int StartDay, int EndMonth, int EndDay, int AnchorMonth, int AnchorDay);

/// <summary>How a holiday matches a film: TMDb keywords, title, overview and genre.</summary>
public sealed record ThemeRule(
    IReadOnlySet<string> StrongTags,
    IReadOnlySet<string> WeakTags,
    Regex? Title,
    Regex? Overview,
    IReadOnlySet<string> Genres,
    bool RequireStrong);

/// <summary>Films of a franchise: TMDb collections, studios or exact original titles.</summary>
public sealed record FranchiseRule(IReadOnlySet<string> Collections, IReadOnlySet<string> Studios, IReadOnlySet<string> OriginalTitles);

/// <summary>One pick: a theme of the «Themed» channel or a top of the «Best» channel.</summary>
public sealed record PickDefinition(
    string Id,
    PickKind Kind,
    PickWindow? Window = null,
    ThemeRule? Rule = null,
    FranchiseRule? Franchise = null,
    int Limit = 20,
    int Minimum = 6);

/// <summary>All picks the plugin knows, in display and rotation order.</summary>
public static class PickCatalog
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Genre names in the metadata languages seen on servers (Kinopoisk writes Russian ones).
    private static readonly string[] HorrorGenres = ["ужасы", "horror"];
    private static readonly string[] WarGenres = ["военный", "war", "kriegsfilm", "krieg"];
    private static readonly string[] RomanceGenres = ["мелодрама", "romance", "liebesfilm"];
    private static readonly string[] ComedyGenres = ["комедия", "comedy", "komödie"];

    /// <summary>Gets the themes of the «Themed» channel: holidays, then franchises.</summary>
    public static IReadOnlyList<PickDefinition> Themes { get; } =
    [
        Holiday("newyear", Window(11, 15, 1, 14, 12, 31),
            ["christmas", "new year's eve", "new year", "santa claus", "christmas eve", "christmas party", "christmas romance", "saving christmas",
             "christmas tree", "christmas spirit", "christmas story", "christmas day", "christmas carol", "christmas carols", "christmas movie",
             "north pole", "reindeer", "elf", "holiday season", "gingerbread"],
            ["holiday", "holidays", "winter", "snow", "gift", "holiday get-together", "snowman", "family reunion"],
            @"рождеств|новогод|нов(ый|ого|ому|ым|ом) год|(?<!\p{L})[её]лк|санта|гринч|щелкунчик|christmas|xmas|santa|grinch|nutcracker|weihnacht|silvester",
            @"рождеств|новогод|нов(ый|ого|ому|ым|ом) год|сочельник|санта|дед(а|ом|у)? мороз|christmas|santa claus|weihnacht|heiligabend|new year's eve"),
        Holiday("valentine", Window(2, 1, 2, 14, 2, 14),
            ["valentine's day", "romcom", "romantic comedy", "love at first sight", "falling in love", "true love", "love story", "first love", "soulmates", "wedding"],
            ["romance", "love", "lovers", "love letter", "dating", "new love", "love affair", "kiss", "marriage proposal"],
            @"валентин|любов|love|valentin|liebe",
            @"день (святого )?валентина|valentine|влюбл",
            RomanceGenres),
        Holiday("defender", Window(2, 15, 2, 23, 2, 23),
            ["military", "army", "soldier", "soldiers", "special forces", "marine corps", "navy seal", "military operation", "war hero",
             "fighter pilot", "commando", "sniper", "military officer", "navy", "military academy", "military life", "world war ii", "world war i"],
            ["war", "combat", "battlefield", "tank", "action hero", "hero", "brotherhood", "rescue mission", "mercenary", "pilot"],
            @"солдат|арми|десант|спецназ|soldier|army|top gun",
            @"арми|солдат|спецназ|военн|десант|фронт",
            WarGenres,
            requireStrong: true),
        Holiday("womensday", Window(3, 2, 3, 8, 3, 8),
            ["female protagonist", "woman director", "strong woman", "sisterhood", "female friendship", "women's rights", "feminism",
             "independent woman", "female empowerment"],
            ["romcom", "romance", "fashion", "wedding", "mother daughter relationship"],
            @"женщин|девушк|леди|woman|women|girl|lady|frau|mädchen",
            null,
            requireStrong: true),
        Holiday("aprilfools", Window(3, 26, 4, 1, 4, 1),
            ["hilarious", "slapstick comedy", "parody", "spoof", "prank", "satire", "absurd", "comedy of errors", "farce", "mockumentary"],
            ["amused", "cheerful", "lighthearted", "comedy", "dark comedy", "buddy comedy"],
            null,
            null,
            ComedyGenres),
        Holiday("cosmonautics", Window(4, 3, 4, 12, 4, 12),
            ["space", "space travel", "astronaut", "nasa", "spacecraft", "space mission", "outer space", "space station", "moon", "mars",
             "cosmonaut", "space exploration", "rocket", "space program", "spaceship", "moon landing", "space race", "international space station"],
            ["alien planet", "planet", "alien", "galaxy", "space colony", "satellite"],
            @"космос|космич|космонавт|(?<!\p{L})луна|(?<!\p{L})марс|гагарин|space|(?<!\p{L})moon|(?<!\p{L})mars(?!\p{L})|apollo|weltraum",
            @"космос|космонавт|астронавт|космическ|орбит|nasa|наса"),
        Franchise("starwars", Window(4, 28, 5, 5, 5, 4), collections: ["10"], titles: ["Rogue One", "Rogue One: A Star Wars Story", "Solo: A Star Wars Story"]),
        Holiday("victory", Window(5, 6, 5, 9, 5, 9),
            ["world war ii", "nazi", "eastern front", "red army", "battle of stalingrad", "soviet union", "soviet army", "great patriotic war",
             "battle of berlin", "wehrmacht", "siege of leningrad", "partisan"],
            ["war", "soldier", "army", "military", "tank"],
            @"войн|сталинград|брест|блокад|ржев|т-34|панфилов",
            @"великой отечественной|(?<!\d)194[1-5](?!\d)|фашист|нацист|сталинград|блокад",
            WarGenres,
            requireStrong: true),
        Holiday("summer", Window(6, 1, 8, 19, 7, 15),
            ["summer", "summer vacation", "beach", "summer camp", "road trip", "vacation", "surfing", "island", "beach house"],
            ["swimming", "swimming pool", "sea", "ocean", "travel", "sailing"],
            @"(?<!\p{L})лет[оа](?!\p{L})|пляж|каникул|summer|beach|sommer|urlaub",
            @"летн|(?<!\p{L})лето|каникул|пляж|отпуск"),
        Franchise("harrypotter", Window(7, 24, 8, 7, 7, 31), collections: ["1241", "435259"]),
        Holiday("school", Window(8, 20, 9, 4, 9, 1),
            ["high school", "school", "boarding school", "teacher", "student", "school life", "school of witchcraft", "school friend", "college",
             "university", "prom", "classroom", "schoolteacher", "first day of school"],
            ["teenager", "coming of age", "teenage romance", "friendship"],
            @"школ|учител|выпускн|school|teacher|schule|lehrer",
            @"школ|учител|ученик|студент|университет|колледж|выпускн"),
        Franchise("tolkien", Window(9, 15, 9, 28, 9, 22), collections: ["119", "121938"]),
        Franchise("bond", Window(10, 1, 10, 8, 10, 5), collections: ["645"]),
        Holiday("halloween", Window(10, 9, 11, 1, 10, 31),
            ["halloween", "haunted house", "trick or treating", "jack-o'-lantern", "ghost story", "haunting", "slasher", "vampire", "werewolf",
             "zombie", "possession", "exorcism", "poltergeist", "pumpkin", "haunted", "horror"],
            ["ghost", "witch", "monster", "supernatural", "curse", "skeleton", "black magic", "witchcraft", "graveyard", "demon", "dark fantasy"],
            @"хэллоуин|хеллоуин|halloween|призрак|вампир|зомби|ведьм|привидени|ghost|vampire|zombie|gespenst",
            @"хэллоуин|хеллоуин|halloween|нечист|привидени|призрак|вампир|зомби",
            HorrorGenres,
            requireStrong: true),
        Franchise("bttf", Window(10, 20, 10, 22, 10, 21), collections: ["264"]),
        Franchise("marvel", studios: ["Marvel Studios"]),
        Franchise("pixar", studios: ["Pixar", "Pixar Animation Studios"]),
        Franchise("ghibli", studios: ["Studio Ghibli"])
    ];

    /// <summary>Gets the tops of the «Best» channel in rotation order.</summary>
    public static IReadOnlyList<PickDefinition> Tops { get; } =
    [
        new("top50", PickKind.Top, Limit: 50),
        new("serverfavorites", PickKind.Top),
        new("popularweek", PickKind.Top),
        new("underrated", PickKind.Top),
        new("books", PickKind.Top),
        new("hiddengems", PickKind.Top)
    ];

    /// <summary>Gets every pick, themes first.</summary>
    public static IEnumerable<PickDefinition> All => Themes.Concat(Tops);

    private static PickWindow Window(int startMonth, int startDay, int endMonth, int endDay, int anchorMonth, int anchorDay)
        => new(startMonth, startDay, endMonth, endDay, anchorMonth, anchorDay);

    private static PickDefinition Holiday(
        string id,
        PickWindow window,
        string[] strong,
        string[] weak,
        string? title,
        string? overview,
        string[]? genres = null,
        bool requireStrong = false)
    {
        var rule = new ThemeRule(
            Set(strong),
            Set(weak),
            title is null ? null : new Regex(title, Options),
            overview is null ? null : new Regex(overview, Options),
            Set(genres ?? []),
            requireStrong);
        return new PickDefinition(id, PickKind.Holiday, window, rule);
    }

    // Franchises are marathons: every film counts, so two are enough and up to 30 are shown.
    private static PickDefinition Franchise(string id, PickWindow? window = null, string[]? collections = null, string[]? studios = null, string[]? titles = null)
        => new(id, PickKind.Franchise, window, Franchise: new FranchiseRule(Set(collections ?? []), Set(studios ?? []), Set(titles ?? [])), Limit: 30, Minimum: 2);

    private static HashSet<string> Set(IEnumerable<string> values) => new(values, StringComparer.OrdinalIgnoreCase);
}
