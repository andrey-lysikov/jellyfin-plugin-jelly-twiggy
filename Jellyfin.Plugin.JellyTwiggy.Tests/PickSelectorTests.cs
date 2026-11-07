using Jellyfin.Plugin.JellyTwiggy.Picks;
using Xunit;

namespace Jellyfin.Plugin.JellyTwiggy.Tests;

public class PickSelectorTests
{
    private static readonly HashSet<string> NoneDisabled = [];
    private static readonly DateOnly Today = new(2026, 11, 7);
    private static readonly TopContext Context = new(Today, 0);

    private static MovieFacts Film(
        string name,
        string[]? tags = null,
        string[]? genres = null,
        float? rating = 7,
        float? critic = null,
        string? collection = null,
        int year = 2000,
        string? original = null,
        string? overview = null,
        string[]? studios = null,
        string[]? countries = null) => new(
            Guid.NewGuid(),
            name,
            original,
            overview,
            new HashSet<string>(tags ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(genres ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(studios ?? [], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(countries ?? [], StringComparer.OrdinalIgnoreCase),
            year,
            rating,
            critic,
            collection);

    private static PickDefinition Theme(string id) => PickCatalog.Themes.Single(t => t.Id == id);

    private static PickDefinition Top(string id) => PickCatalog.Tops.Single(t => t.Id == id);

    private static List<MovieFacts> ChristmasFilms(int count) =>
        Enumerable.Range(0, count).Select(i => Film($"Рождество {i}", ["christmas", "santa claus"])).ToList();

    [Fact]
    public void LoneHolidayTagIsNotEnough()
    {
        var gump = Film("Форрест Гамп", ["christmas", "vietnam war"], rating: 8.8f);
        var klaus = Film("Клаус", ["christmas", "santa claus", "elf"], rating: 8.2f);

        var picked = PickSelector.SelectTheme(Theme("newyear"), [gump, klaus]);

        Assert.Equal(new[] { klaus.Id }, picked);
    }

    [Fact]
    public void RussianOverviewCountsAndWordsMustStartWithTheStem()
    {
        var irony = Film("Ирония судьбы, или С легким паром!", ["new year's eve"], overview: "В новогоднюю ночь…");
        // "Белка" and "Стрелка" contain "елк" but are not about a fir tree.
        var squirrel = Film("Белка и Стрелка", ["dog"], overview: "Собаки летят в космос");

        Assert.Equal(3 + 2, PickSelector.Score(Theme("newyear").Rule!, irony).Total);
        Assert.Equal(new[] { irony.Id }, PickSelector.SelectTheme(Theme("newyear"), [irony, squirrel]));
    }

    [Fact]
    public void HalloweenNeedsAStrongSignal()
    {
        var wizards = Film("Школа волшебников", ["witch", "ghost", "monster", "supernatural", "curse"]);
        var horror = Film("Призрак оперы", ["ghost"], genres: ["ужасы"]);

        var picked = PickSelector.SelectTheme(Theme("halloween"), [wizards, horror]);

        Assert.Equal(new[] { horror.Id }, picked);
    }

    [Fact]
    public void RussiaDayTakesOnlyFilmsMadeEntirelyInRussia()
    {
        var russian = Film("Мамы", countries: ["Россия", "Russia"], rating: 6.5f);
        var coProduction = Film("Миссия невыполнима", countries: ["США", "Россия", "United States of America", "Russia"], rating: 7.5f);
        var soviet = Film("Служебный роман", countries: ["СССР", "Soviet Union"], rating: 8.2f);
        var unknown = Film("Без страны", rating: 9f);

        Assert.Equal(new[] { russian.Id }, PickSelector.SelectTheme(Theme("russiaday"), [russian, coProduction, soviet, unknown]));
        Assert.Equal(new[] { soviet.Id }, PickSelector.SelectTheme(Theme("unityday"), [russian, coProduction, soviet, unknown]));
    }

    [Fact]
    public void ChildrensDayNeedsAFamilyGenre()
    {
        var shawshank = Film("Побег из Шоушенка", ["friendship", "children"], genres: ["драма"], rating: 9.3f);
        var cartoon = Film("Простоквашино", genres: ["мультфильм"], rating: 8.2f);

        Assert.Equal(new[] { cartoon.Id }, PickSelector.SelectTheme(Theme("childrensday"), [shawshank, cartoon]));
    }

    [Fact]
    public void AtMostTwoFilmsPerFranchiseInAHoliday()
    {
        var series = Enumerable.Range(0, 3).Select(i => Film($"Ёлки {i}", ["new year's eve", "christmas"], collection: "107465")).ToList();
        var other = Film("Гринч", ["christmas", "santa claus"]);

        var picked = PickSelector.SelectTheme(Theme("newyear"), [.. series, other]);

        Assert.Equal(3, picked.Count);
        Assert.Contains(other.Id, picked);
    }

    [Fact]
    public void FranchiseGoesInReleaseOrder()
    {
        var films = new[]
        {
            Film("Возвращение короля", collection: "119", year: 2003),
            Film("Братство кольца", collection: "119", year: 2001),
            Film("Нежданное путешествие", collection: "121938", year: 2012),
            Film("Чужой", collection: "8091", year: 1979)
        };

        var chosen = PickSelector.ChooseThemes(new DateOnly(2027, 9, 22), films, NoneDisabled);

        Assert.Equal("tolkien", chosen[0].Pick.Id);
        Assert.Equal(new[] { films[1].Id, films[0].Id, films[2].Id }, chosen[0].Items);
    }

    [Fact]
    public void StarWarsSpinOffsMatchByOriginalTitle()
    {
        var rogue = Film("Изгой-один", original: "Rogue One: A Star Wars Story", year: 2016);
        var episode = Film("Новая надежда", collection: "10", year: 1977);
        var solo = Film("Соло", original: "Solo Mio");

        var picked = PickSelector.SelectTheme(Theme("starwars"), [rogue, episode, solo]);

        Assert.Equal(new[] { episode.Id, rogue.Id }, picked);
    }

    [Fact]
    public void HolidayWithTooFewFilmsGivesWayToAFranchise()
    {
        var films = ChristmasFilms(5).Append(Film("Toy Story", studios: ["Pixar"])).Append(Film("Up", studios: ["Pixar"])).ToList();

        var chosen = PickSelector.ChooseThemes(new DateOnly(2026, 12, 20), films, NoneDisabled);

        Assert.Equal("pixar", chosen[0].Pick.Id);
    }

    [Fact]
    public void HolidayWithEnoughFilmsIsChosen()
    {
        var chosen = PickSelector.ChooseThemes(new DateOnly(2026, 12, 20), ChristmasFilms(6), NoneDisabled);

        Assert.Equal("newyear", chosen[0].Pick.Id);
        Assert.Equal(6, chosen[0].Items.Count);
    }

    [Fact]
    public void DisabledHolidayIsSkipped()
    {
        var chosen = PickSelector.ChooseThemes(new DateOnly(2026, 12, 20), ChristmasFilms(6), new HashSet<string> { "newyear" });

        Assert.Empty(chosen);
    }

    [Fact]
    public void NearbyHolidaysAreShownTogether()
    {
        // 2026: Friday 13 February, Valentine's Day, Chinese New Year on the 17th; 23 February is ten days on.
        var horror = Enumerable.Range(0, 6).Select(i => Film($"Ужасы {i}", ["slasher", "haunted house"], genres: ["ужасы"])).ToList();
        var romance = Enumerable.Range(0, 6).Select(i => Film($"Любовь {i}", ["valentine's day", "romcom"])).ToList();
        var china = Enumerable.Range(0, 6).Select(i => Film($"Кунг-фу {i}", ["china", "wuxia"])).ToList();
        var army = Enumerable.Range(0, 6).Select(i => Film($"Армия {i}", ["military", "army"])).ToList();

        var chosen = PickSelector.ChooseThemes(new DateOnly(2026, 2, 10), [.. horror, .. romance, .. china, .. army], NoneDisabled);

        Assert.Equal(new[] { "friday13", "valentine", "chinesenewyear" }, chosen.Select(c => c.Pick.Id));
    }

    [Fact]
    public void AFilmGoesToTheFirstThemeOnly()
    {
        // A Christmas horror fits both Friday 13th and Valentine's Day here; the nearer one keeps it.
        var both = Film("Чёрное Рождество", ["slasher", "valentine's day", "romcom"], genres: ["ужасы"]);
        var horror = Enumerable.Range(0, 6).Select(i => Film($"Ужасы {i}", ["slasher", "haunted house"], genres: ["ужасы"])).ToList();
        var romance = Enumerable.Range(0, 6).Select(i => Film($"Любовь {i}", ["valentine's day", "romcom"])).ToList();

        var chosen = PickSelector.ChooseThemes(new DateOnly(2026, 2, 10), [both, .. horror, .. romance], NoneDisabled);

        Assert.Contains(both.Id, chosen[0].Items);
        Assert.DoesNotContain(both.Id, chosen[1].Items);
    }

    [Fact]
    public void FranchisesRotateWeekly()
    {
        var films = new[]
        {
            Film("Toy Story", studios: ["Pixar"]), Film("Up", studios: ["Pixar"]),
            Film("Iron Man", studios: ["Marvel Studios"]), Film("Thor", studios: ["Marvel Studios"])
        };

        var monday = PickSelector.ChooseThemes(new DateOnly(2027, 1, 18), films, NoneDisabled)[0].Pick.Id;
        var sunday = PickSelector.ChooseThemes(new DateOnly(2027, 1, 24), films, NoneDisabled)[0].Pick.Id;
        var nextMonday = PickSelector.ChooseThemes(new DateOnly(2027, 1, 25), films, NoneDisabled)[0].Pick.Id;

        Assert.Equal(monday, sunday);
        Assert.NotEqual(monday, nextMonday);
    }

    [Fact]
    public void TopFiftyNeedsBothRatingsAndCountsServerViews()
    {
        var noCritic = Film("Без критиков", rating: 9.5f);
        var good = Film("Хороший", rating: 8.0f, critic: 80);
        var loved = Film("Любимый", rating: 7.8f, critic: 80);
        var usage = new UsageFacts();
        usage.Viewers[loved.Id] = 4;

        var picked = PickSelector.SelectTop(Top("top50"), [noCritic, good, loved], usage, Context).Items;

        Assert.Equal(new[] { loved.Id, good.Id }, picked);
    }

    [Fact]
    public void UnderratedNeedsADecentAudienceRating()
    {
        var obscure = Film("Пара рецензий", rating: 4.5f, critic: 100);
        var cult = Film("Культовый", rating: 7.0f, critic: 95);
        var popular = Film("Все любят", rating: 8.5f, critic: 90);

        Assert.Equal(new[] { cult.Id }, PickSelector.SelectTop(Top("underrated"), [obscure, cult, popular], new UsageFacts(), Context).Items);
    }

    [Fact]
    public void HiddenGemsSkipAnythingWatched()
    {
        var seen = Film("Видели", rating: 8.5f);
        var unseen = Film("Не видели", rating: 8.0f);
        var weak = Film("Слабый", rating: 6.0f);
        var usage = new UsageFacts();
        usage.Touched.Add(seen.Id);

        Assert.Equal(new[] { unseen.Id }, PickSelector.SelectTop(Top("hiddengems"), [seen, unseen, weak], usage, Context).Items);
    }

    [Fact]
    public void ServerFavoritesNeedMoreThanOneViewer()
    {
        var once = Film("Раз");
        var twice = Film("Два");
        var favorite = Film("Избранное");
        var usage = new UsageFacts();
        usage.Viewers[once.Id] = 1;
        usage.Viewers[twice.Id] = 2;
        usage.Favorites[favorite.Id] = 1;

        Assert.Equal(new[] { twice.Id, favorite.Id }, PickSelector.SelectTop(Top("serverfavorites"), [once, twice, favorite], usage, Context).Items);
    }

    [Fact]
    public void DirectorOfTheWeekGoesRoundTheDirectors()
    {
        var spielberg = Enumerable.Range(0, 5).Select(i => Film($"Спилберг {i}", rating: 8, critic: 80) with { Directors = ["Стивен Спилберг"] }).ToList();
        var bay = Enumerable.Range(0, 5).Select(i => Film($"Бэй {i}", rating: 6, critic: 40) with { Directors = ["Майкл Бэй"] }).ToList();
        var once = Film("Дебют", rating: 9, critic: 90) with { Directors = ["Новичок"] };
        List<MovieFacts> films = [.. spielberg, .. bay, once];

        var first = PickSelector.SelectTop(Top("director"), films, new UsageFacts(), Context);
        var second = PickSelector.SelectTop(Top("director"), films, new UsageFacts(), Context with { Round = 1 });
        var third = PickSelector.SelectTop(Top("director"), films, new UsageFacts(), Context with { Round = 2 });

        Assert.Equal("Стивен Спилберг", first.Subject);
        Assert.Equal(5, first.Items.Count);
        Assert.Equal("Майкл Бэй", second.Subject);
        Assert.Equal("Стивен Спилберг", third.Subject);
    }

    [Fact]
    public void StarOfTheWeekNeedsSixFilms()
    {
        var hanks = Enumerable.Range(0, 6).Select(i => Film($"Хэнкс {i}", rating: 7) with { Actors = ["Том Хэнкс"] }).ToList();
        var newcomer = Enumerable.Range(0, 5).Select(i => Film($"Новичок {i}", rating: 9) with { Actors = ["Новичок"] }).ToList();

        var choice = PickSelector.SelectTop(Top("star"), [.. hanks, .. newcomer], new UsageFacts(), Context);

        Assert.Equal("Том Хэнкс", choice.Subject);
        Assert.Equal(6, choice.Items.Count);
        Assert.Equal(new[] { "Том Хэнкс" }, PickSelector.PersonCandidates(Top("star"), [.. hanks, .. newcomer], m => m.Actors));
    }

    [Fact]
    public void RecentTakesThisAndLastYear()
    {
        var thisYear = Film("Этот год", year: 2026);
        var lastYear = Film("Прошлый год", year: 2025);
        var older = Film("Позапрошлый", year: 2024);

        var items = PickSelector.SelectTop(Top("recent"), [thisYear, lastYear, older], new UsageFacts(), Context).Items;

        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(older.Id, items);
    }

    [Fact]
    public void TheSameFilmTwiceCountsOnce()
    {
        var copy1 = Film("Тенцинг", rating: 7.7f, critic: 80, year: 2026);
        var copy2 = Film("Тенцинг", rating: 7.7f, critic: 80, year: 2026);

        var items = PickSelector.SelectTop(Top("top50"), [copy1, copy2], new UsageFacts(), Context).Items;

        Assert.Single(items);
    }

    [Fact]
    public void LongAgoTakesFilmsNobodyWatchedForHalfAYear()
    {
        var old = Film("Давно");
        var recent = Film("Недавно");
        var never = Film("Никогда");
        var usage = new UsageFacts();
        usage.Viewers[old.Id] = 3;
        usage.LastWatched[old.Id] = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        usage.Viewers[recent.Id] = 4;
        usage.LastWatched[recent.Id] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new[] { old.Id }, PickSelector.SelectTop(Top("longago"), [old, recent, never], usage, Context).Items);
    }

    [Fact]
    public void FilmsWatchedThisMonthAreLeftOut()
    {
        var lastWeek = Film("На прошлой неделе");
        var twoMonths = Film("Два месяца назад");
        var never = Film("Никогда");
        var usage = new UsageFacts();
        usage.LastWatched[lastWeek.Id] = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        usage.LastWatched[twoMonths.Id] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var left = PickSelector.NotRecentlyWatched([lastWeek, twoMonths, never], usage, Today);

        Assert.Equal(new[] { twoMonths.Id, never.Id }, left.Select(m => m.Id));
    }

    [Fact]
    public void RotationSkipsTopsWithTooFewFilmsAndWraps()
    {
        // "top50" takes every rated film; after it only "underrated" and "books" have enough.
        var books = Enumerable.Range(0, 6).Select(i => Film($"Книга {i}", ["based on novel or book"], rating: 6, critic: 50)).ToList();
        var underrated = Enumerable.Range(0, 6).Select(i => Film($"Критики {i}", rating: 7, critic: 90)).ToList();
        List<MovieFacts> films = [.. books, .. underrated];
        var tops = PickCatalog.Tops.Select(t => t.Id).ToList();

        var first = PickSelector.NextTop(-1, films, new UsageFacts(), NoneDisabled, Today, _ => 0);
        var second = PickSelector.NextTop(first!.Value.Index, films, new UsageFacts(), NoneDisabled, Today, _ => 0);
        var third = PickSelector.NextTop(second!.Value.Index, films, new UsageFacts(), NoneDisabled, Today, _ => 0);

        Assert.Equal("top50", tops[first.Value.Index]);
        Assert.Equal("underrated", tops[second.Value.Index]);
        Assert.Equal("books", tops[third!.Value.Index]);
    }

    [Fact]
    public void RotationSkipsDisabledTops()
    {
        var films = Enumerable.Range(0, 6).Select(i => Film($"Фильм {i}", rating: 8, critic: 80)).ToList();

        var next = PickSelector.NextTop(-1, films, new UsageFacts(), new HashSet<string> { "top50" }, Today, _ => 0);

        Assert.NotEqual("top50", PickCatalog.Tops[next!.Value.Index].Id);
    }
}
