namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>How well a film matches a holiday; Strong counts strong tags and a matching genre.</summary>
public readonly record struct ThemeScore(int Total, int Strong);

/// <summary>Chooses the films of each pick. Pure: works on facts only, so it is unit tested.</summary>
public static class PickSelector
{
    /// <summary>Minimum score for a film to belong to a holiday.</summary>
    public const int RelevanceThreshold = 5;

    // One franchise must not take over a list of the best.
    private const int MaxPerCollection = 2;

    /// <summary>Gets today's theme and its films: the nearest holiday with enough films, else a franchise of the week.</summary>
    public static (PickDefinition Pick, IReadOnlyList<Guid> Items)? ChooseTheme(DateOnly date, IReadOnlyList<MovieFacts> movies, IReadOnlySet<string> disabled)
    {
        var themes = PickCatalog.Themes.Where(t => !disabled.Contains(t.Id)).ToList();
        foreach (var theme in PickCalendar.ActiveThemes(date, themes))
        {
            var items = SelectTheme(theme, movies);
            if (items.Count >= theme.Minimum)
            {
                return (theme, items);
            }
        }

        var franchises = themes
            .Where(t => t.Kind == PickKind.Franchise)
            .Select(t => (Pick: t, Items: SelectTheme(t, movies)))
            .Where(f => f.Items.Count >= f.Pick.Minimum)
            .ToList();
        if (franchises.Count == 0)
        {
            return null;
        }

        var chosen = franchises[PickCalendar.WeekIndex(date) % franchises.Count];
        return (chosen.Pick, chosen.Items);
    }

    /// <summary>Gets the next top with enough films after <paramref name="previous"/>, wrapping around; null when none has.</summary>
    public static (int Index, IReadOnlyList<Guid> Items)? NextTop(int previous, IReadOnlyList<MovieFacts> movies, UsageFacts usage, IReadOnlySet<string> disabled)
    {
        var tops = PickCatalog.Tops;
        for (var step = 1; step <= tops.Count; step++)
        {
            var index = (((previous + step) % tops.Count) + tops.Count) % tops.Count;
            var top = tops[index];
            if (disabled.Contains(top.Id))
            {
                continue;
            }

            var items = SelectTop(top, movies, usage);
            if (items.Count >= top.Minimum)
            {
                return (index, items);
            }
        }

        return null;
    }

    /// <summary>Gets the films of a holiday (by relevance and rating) or a franchise (by release year).</summary>
    public static IReadOnlyList<Guid> SelectTheme(PickDefinition pick, IReadOnlyList<MovieFacts> movies)
    {
        if (pick.Franchise is { } franchise)
        {
            return movies
                .Where(m => IsInFranchise(franchise, m))
                .OrderBy(m => m.Year ?? int.MaxValue)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .Take(pick.Limit)
                .Select(m => m.Id)
                .ToList();
        }

        if (pick.Rule is not { } rule)
        {
            return [];
        }

        var ranked = movies
            .Select(m => (Movie: m, Score: Score(rule, m)))
            .Where(x => x.Score.Total >= RelevanceThreshold && (!rule.RequireStrong || x.Score.Strong > 0))
            .OrderByDescending(x => (x.Movie.CommunityRating ?? 5) + (Math.Min(x.Score.Total, 12) / 4.0))
            .Select(x => x.Movie);
        return CapPerCollection(ranked, pick.Limit);
    }

    /// <summary>Scores a film for a holiday: strong tag 3, weak tag 1, genre 3, title 4, overview 2.</summary>
    public static ThemeScore Score(ThemeRule rule, MovieFacts movie)
    {
        var strong = movie.Tags.Count(rule.StrongTags.Contains);
        var weak = movie.Tags.Count(rule.WeakTags.Contains);
        var genre = movie.Genres.Any(rule.Genres.Contains);
        var total = (3 * strong) + weak + (genre ? 3 : 0);
        if (rule.Title?.IsMatch($"{movie.Name} {movie.OriginalTitle}") == true)
        {
            total += 4;
        }

        if (movie.Overview is { } overview && rule.Overview?.IsMatch(overview) == true)
        {
            total += 2;
        }

        return new ThemeScore(total, strong + (genre ? 1 : 0));
    }

    /// <summary>Gets the films of a top of the «Best» channel.</summary>
    public static IReadOnlyList<Guid> SelectTop(PickDefinition pick, IReadOnlyList<MovieFacts> movies, UsageFacts usage)
    {
        double Popularity(MovieFacts m) => Math.Min(
            1.0,
            (0.25 * usage.Viewers.GetValueOrDefault(m.Id)) + (0.1 * usage.Rewatches.GetValueOrDefault(m.Id)) + (0.25 * usage.Favorites.GetValueOrDefault(m.Id)));

        double Love(MovieFacts m) =>
            (3 * usage.Favorites.GetValueOrDefault(m.Id)) + (2 * usage.Viewers.GetValueOrDefault(m.Id)) + usage.Rewatches.GetValueOrDefault(m.Id);

        IEnumerable<MovieFacts> ranked = pick.Id switch
        {
            "top50" => movies
                .Where(m => m.CommunityRating is not null && m.CriticRating is not null)
                .OrderByDescending(m => Quality(m) + Popularity(m)),
            // One viewer alone is not yet "loved": a favorite or a second viewer is needed.
            "serverfavorites" => movies
                .Where(m => Love(m) >= 3)
                .OrderByDescending(Love)
                .ThenByDescending(Quality),
            "popularweek" => movies
                .Where(m => usage.WeekViewers.GetValueOrDefault(m.Id) > 0)
                .OrderByDescending(m => usage.WeekViewers.GetValueOrDefault(m.Id))
                .ThenByDescending(m => usage.WeekPlays.GetValueOrDefault(m.Id))
                .ThenByDescending(Quality),
            // Critics' 100 from a handful of reviews is common, so the audience rating must still be decent.
            "underrated" => movies
                .Where(m => m is { CriticRating: >= 75f, CommunityRating: >= 6.5f } && Gap(m) >= 1.0)
                .OrderByDescending(Gap)
                .ThenByDescending(m => m.CriticRating),
            "books" => movies
                .Where(m => m.Tags.Contains("based on novel or book"))
                .OrderByDescending(m => Quality(m) + Popularity(m)),
            "hiddengems" => movies
                .Where(m => m.CommunityRating >= 7.5 && !usage.Touched.Contains(m.Id))
                .OrderByDescending(Quality),
            _ => []
        };

        // The weekly chart shows what people watched, franchises included.
        return pick.Id == "popularweek"
            ? ranked.Take(pick.Limit).Select(m => m.Id).ToList()
            : CapPerCollection(ranked, pick.Limit);
    }

    // How far critics rate a film above its audience, on the 0–10 scale.
    private static double Gap(MovieFacts movie) => ((movie.CriticRating ?? 0) / 10.0) - (movie.CommunityRating ?? 0);

    /// <summary>Gets the rating used for tops: 70% audience, 30% critics, or the audience alone.</summary>
    public static double Quality(MovieFacts movie)
    {
        return movie is { CommunityRating: { } rating, CriticRating: { } critic }
            ? (0.7 * rating) + (0.03 * critic)
            : movie.CommunityRating ?? 0;
    }

    private static bool IsInFranchise(FranchiseRule rule, MovieFacts movie)
    {
        return (movie.CollectionId is { } collection && rule.Collections.Contains(collection))
            || movie.Studios.Any(rule.Studios.Contains)
            || (movie.OriginalTitle is { } title && rule.OriginalTitles.Contains(title));
    }

    private static List<Guid> CapPerCollection(IEnumerable<MovieFacts> ranked, int limit)
    {
        var perCollection = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<Guid>();
        foreach (var movie in ranked)
        {
            if (movie.CollectionId is { } collection)
            {
                var count = perCollection.GetValueOrDefault(collection);
                if (count >= MaxPerCollection)
                {
                    continue;
                }

                perCollection[collection] = count + 1;
            }

            result.Add(movie.Id);
            if (result.Count == limit)
            {
                break;
            }
        }

        return result;
    }
}
