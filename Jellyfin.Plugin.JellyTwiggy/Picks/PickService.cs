using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyTwiggy.Configuration;
using Jellyfin.Plugin.JellyTwiggy.History;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Computes the picks from the library and the playback log, saves them and syncs the collections.</summary>
public class PickService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly PlaybackHistoryStore _history;
    private readonly PickStore _store;
    private readonly PickCollections _collections;
    private readonly ILogger<PickService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="PickService"/> class.</summary>
    public PickService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        IServerConfigurationManager serverConfiguration,
        PlaybackHistoryStore history,
        PickStore store,
        PickCollections collections,
        ILogger<PickService> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _serverConfiguration = serverConfiguration;
        _history = history;
        _store = store;
        _collections = collections;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Picks today's theme; the content changes only when the theme or its films do.</summary>
    public async Task RefreshThemedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshThemedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Fills «Best»: the next top when rotating, else the current one recomputed.</summary>
    public async Task UpdateBestAsync(bool rotate, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await UpdateBestCoreAsync(rotate, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Catches up after downtime and removes deleted films from the saved picks.</summary>
    public async Task EnsureFreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = _store.Get();
            if (state.Themed is null || state.Themed.Groups.Count == 0 || state.Themed.Date != Today)
            {
                await RefreshThemedCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SyncExistingAsync(PickCollectionKind.Themed, cancellationToken).ConfigureAwait(false);
            }

            if (state.Best is null || state.Best.Groups.Count == 0)
            {
                await UpdateBestCoreAsync(rotate: CurrentTopIndex(state) < 0, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SyncExistingAsync(PickCollectionKind.Best, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Recomputes both collections after a settings change, without moving to the next top.</summary>
    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshThemedCoreAsync(cancellationToken).ConfigureAwait(false);
            await UpdateBestCoreAsync(rotate: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshThemedCoreAsync(CancellationToken cancellationToken)
    {
        var config = Config;
        var all = LoadMovies(config, out var libraries);
        var movies = PickSelector.NotRecentlyWatched(all, LoadUsage(all, libraries), Today);
        var chosen = PickSelector.ChooseThemes(Today, movies, Disabled(config));
        var state = _store.Get();
        var themed = config.EnablePicks
            ? chosen.Count > 0
                ? MakeResult(chosen.Select(c => (c.Pick.Id, c.Items, (string?)null)).ToList(), state.Themed)
                : Previous(state.Themed, config, all)
            : null;
        _logger.LogInformation("Jelly Twiggy: themed picks are {Picks}", Describe(themed));
        await SyncAndSaveAsync(PickCollectionKind.Themed, themed, state, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateBestCoreAsync(bool rotate, CancellationToken cancellationToken)
    {
        var config = Config;
        var all = LoadMovies(config, out var libraries, withPeople: true);
        var usage = LoadUsage(all, libraries);
        var movies = PickSelector.NotRecentlyWatched(all, usage, Today);
        var state = _store.Get();
        var current = CurrentTopIndex(state);
        var currentId = current < 0 ? null : PickCatalog.Tops[current].Id;

        // Without rotation the search starts at the current top, so it stays (with the same person) unless it no longer has enough films.
        int RoundOf(string id) => !rotate && id == currentId
            ? state.Rounds.GetValueOrDefault(id)
            : state.Rounds.TryGetValue(id, out var round) ? round + 1 : 0;
        var previous = rotate ? current : Math.Max(current, 0) - 1;
        var next = PickSelector.NextTop(previous, movies, usage, Disabled(config), Today, RoundOf);

        var rounds = new Dictionary<string, int>(state.Rounds);
        PickResult? best = null;
        if (next is { } n)
        {
            var id = PickCatalog.Tops[n.Index].Id;
            rounds[id] = RoundOf(id);
            best = MakeResult([(id, n.Choice.Items, n.Choice.Subject)], state.Best);
        }
        else
        {
            best = Previous(state.Best, config, all);
        }

        if (!config.EnablePicks)
        {
            best = null;
        }

        _logger.LogInformation("Jelly Twiggy: best pick is {Picks}", Describe(best));
        await SyncAndSaveAsync(PickCollectionKind.Best, best, state, rounds, cancellationToken).ConfigureAwait(false);
    }

    // The current top is known by id: the list of tops may change between plugin versions.
    private static int CurrentTopIndex(PickState state)
    {
        var id = state.Best?.Groups.FirstOrDefault()?.PickId;
        return id is null ? -1 : PickCatalog.Tops.ToList().FindIndex(t => t.Id == id);
    }

    private static string Describe(PickResult? pick) =>
        pick is null ? "none" : string.Join(", ", pick.Groups.Select(g => $"{g.PickId} ({g.ItemIds.Count})"));

    // An unchanged pick keeps its timestamp.
    // A person top is named after its director or star: "Director of the week — Steven Spielberg".
    private PickResult MakeResult(IReadOnlyList<(string PickId, IReadOnlyList<Guid> Items, string? Subject)> groups, PickResult? previous)
    {
        var culture = _serverConfiguration.Configuration.UICulture;
        string NameOf(string pickId, string? subject) =>
            subject is null ? PickNames.Get(pickId, culture) : string.Format(CultureInfo.InvariantCulture, PickNames.Get(pickId + "Title", culture), subject);

        if (previous is not null
            && previous.Groups.Count == groups.Count
            && previous.Groups.Zip(groups).All(x => x.First.PickId == x.Second.PickId
                && x.First.Name == NameOf(x.Second.PickId, x.Second.Subject)
                && x.First.ItemIds.SequenceEqual(x.Second.Items)))
        {
            return new PickResult { Groups = previous.Groups, GeneratedUtc = previous.GeneratedUtc, Date = Today };
        }

        var now = DateTime.UtcNow;
        return new PickResult
        {
            Groups = groups.Select(g => new PickGroup
            {
                PickId = g.PickId,
                Name = NameOf(g.PickId, g.Subject),
                ItemIds = [.. g.Items]
            }).ToList(),
            GeneratedUtc = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
            Date = Today
        };
    }

    private static PickResult? Previous(PickResult? previous, PluginConfiguration config, List<MovieFacts> movies)
    {
        if (config is not { EnablePicks: true, PickSourceLibraries.Length: > 0 })
        {
            return null;
        }

        return PickCollectionRules.RetainPrevious(
            previous,
            movies.Select(m => m.Id).ToHashSet(),
            Disabled(config));
    }

    private async Task SyncExistingAsync(PickCollectionKind kind, CancellationToken cancellationToken)
    {
        var config = Config;
        var movies = LoadMovies(config, out _);
        var state = _store.Get();
        var result = Previous(kind == PickCollectionKind.Themed ? state.Themed : state.Best, config, movies);
        await SyncAndSaveAsync(kind, result, state, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncAndSaveAsync(
        PickCollectionKind kind,
        PickResult? result,
        PickState state,
        Dictionary<string, int>? rounds,
        CancellationToken cancellationToken)
    {
        var previousId = kind == PickCollectionKind.Themed ? state.ThemedCollectionId : state.BestCollectionId;
        var collectionId = await _collections.SyncAsync(kind, result, previousId, cancellationToken).ConfigureAwait(false);
        _store.Save(new PickState
        {
            Themed = kind == PickCollectionKind.Themed ? result : state.Themed,
            Best = kind == PickCollectionKind.Best ? result : state.Best,
            Rounds = rounds ?? state.Rounds,
            ThemedCollectionId = kind == PickCollectionKind.Themed ? collectionId : state.ThemedCollectionId,
            BestCollectionId = kind == PickCollectionKind.Best ? collectionId : state.BestCollectionId,
            LegacyChannelsRemoved = state.LegacyChannelsRemoved
        });
    }

    private static HashSet<string> Disabled(PluginConfiguration config) => new(config.DisabledPicks, StringComparer.Ordinal);

    // Only the libraries the admin chose.
    private List<MovieFacts> LoadMovies(PluginConfiguration config, out List<BaseItem> libraries, bool withPeople = false)
    {
        libraries = config.PickSourceLibraries
            .Select(id => _libraryManager.GetItemById(id))
            .OfType<BaseItem>()
            .ToList();
        if (libraries.Count == 0)
        {
            return [];
        }

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            IsVirtualItem = false
        };
        var items = _libraryManager.GetItemList(query, libraries);
        var people = withPeople ? LoadPeople(items) : null;
        return items
            .Select(item => new MovieFacts(
                item.Id,
                item.Name ?? string.Empty,
                item.OriginalTitle,
                item.Overview,
                Set(item.Tags),
                Set(item.Genres),
                Set(item.Studios),
                Set(item.ProductionLocations),
                item.ProductionYear,
                item.CommunityRating,
                item.CriticRating,
                item.GetProviderId(MetadataProvider.TmdbCollection))
            {
                Directors = people?.GetValueOrDefault(item.Id).Directors ?? [],
                Actors = people?.GetValueOrDefault(item.Id).Actors ?? []
            })
            .ToList();
    }

    // Directors and the first five actors in the credits (later ones are cameos and voices), in batches.
    private Dictionary<Guid, (IReadOnlyList<string> Directors, IReadOnlyList<string> Actors)> LoadPeople(IReadOnlyList<BaseItem> items)
    {
        var result = new Dictionary<Guid, (IReadOnlyList<string> Directors, IReadOnlyList<string> Actors)>();
        foreach (var batch in items.Select(i => i.Id).Chunk(500))
        {
            foreach (var (itemId, people) in _libraryManager.GetPeopleByItems(batch))
            {
                var directors = people.Where(p => p.Type == PersonKind.Director).Select(p => p.Name).ToList();
                var actors = people.Where(p => p.Type == PersonKind.Actor)
                    .OrderBy(p => p.SortOrder ?? int.MaxValue)
                    .Take(5)
                    .Select(p => p.Name)
                    .ToList();
                result[itemId] = (directors, actors);
            }
        }

        return result;
    }

    // Jellyfin's played, favorite and play count marks of every user, merged with our log (the log wins where it has the pair).
    private UsageFacts LoadUsage(List<MovieFacts> movies, List<BaseItem> libraries)
    {
        var usage = new UsageFacts();
        if (movies.Count == 0)
        {
            return usage;
        }

        var ids = movies.Select(m => m.Id).ToHashSet();
        var viewers = new Dictionary<Guid, HashSet<Guid>>();
        var logged = new HashSet<(Guid UserId, Guid ItemId)>();

        foreach (var group in _history.Find(r => ids.Contains(r.ItemId)).GroupBy(r => (r.UserId, r.ItemId)))
        {
            var (userId, itemId) = group.Key;
            logged.Add(group.Key);
            usage.Touched.Add(itemId);
            var views = group.Count(r => r.Completed);
            if (views > 0)
            {
                Add(viewers, itemId, userId);
                usage.Rewatches[itemId] = usage.Rewatches.GetValueOrDefault(itemId) + views - 1;
            }

            Latest(usage.LastWatched, itemId, group.Max(r => r.EndedUtc));
        }

        foreach (var user in _userManager.GetUsers())
        {
            foreach (var item in UserMovies(user, libraries, isPlayed: true))
            {
                usage.Touched.Add(item.Id);
                var data = _userDataManager.GetUserData(user, item);
                if (data?.LastPlayedDate is { } lastPlayed)
                {
                    Latest(usage.LastWatched, item.Id, lastPlayed);
                }

                if (logged.Contains((user.Id, item.Id)))
                {
                    continue;
                }

                // History older than the log: PlayCount counts starts, so it adds rewatches only beyond the first.
                Add(viewers, item.Id, user.Id);
                usage.Rewatches[item.Id] = usage.Rewatches.GetValueOrDefault(item.Id) + Math.Max((data?.PlayCount ?? 1) - 1, 0);
            }

            foreach (var item in UserMovies(user, libraries, isFavorite: true))
            {
                usage.Favorites[item.Id] = usage.Favorites.GetValueOrDefault(item.Id) + 1;
            }
        }

        foreach (var (itemId, users) in viewers)
        {
            usage.Viewers[itemId] = users.Count;
        }

        return usage;
    }

    private IReadOnlyList<BaseItem> UserMovies(Jellyfin.Database.Implementations.Entities.User user, List<BaseItem> libraries, bool? isPlayed = null, bool? isFavorite = null)
    {
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            IsVirtualItem = false,
            IsPlayed = isPlayed,
            IsFavorite = isFavorite
        };
        return _libraryManager.GetItemList(query, libraries);
    }

    private static void Latest(Dictionary<Guid, DateTime> map, Guid key, DateTime value)
    {
        if (!map.TryGetValue(key, out var current) || value > current)
        {
            map[key] = value;
        }
    }

    private static void Add(Dictionary<Guid, HashSet<Guid>> map, Guid key, Guid value)
    {
        if (!map.TryGetValue(key, out var set))
        {
            map[key] = set = [];
        }

        set.Add(value);
    }

    private static HashSet<string> Set(IEnumerable<string>? values) => new(values ?? [], StringComparer.OrdinalIgnoreCase);
}
