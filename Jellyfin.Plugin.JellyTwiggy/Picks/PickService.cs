using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyTwiggy.Configuration;
using Jellyfin.Plugin.JellyTwiggy.History;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Computes the picks from the library and the playback log, saves them and syncs the channels.</summary>
public class PickService
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IChannelManager _channelManager;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly PlaybackHistoryStore _history;
    private readonly PickStore _store;
    private readonly PickCopies _copies;
    private readonly ILogger<PickService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="PickService"/> class.</summary>
    public PickService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        IChannelManager channelManager,
        IServerConfigurationManager serverConfiguration,
        PlaybackHistoryStore history,
        PickStore store,
        PickCopies copies,
        ILogger<PickService> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _channelManager = channelManager;
        _serverConfiguration = serverConfiguration;
        _history = history;
        _store = store;
        _copies = copies;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Picks today's theme for the «Themed» channel; the content changes only when the theme or its films do.</summary>
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

    /// <summary>Fills the «Best» channel: the next top when rotating, else the current one recomputed.</summary>
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

    /// <summary>Catches up after downtime: today's theme, and a first top if there is none yet.</summary>
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

            if (state.Best is null || state.Best.Groups.Count == 0)
            {
                await UpdateBestCoreAsync(rotate: state.BestIndex < 0, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Recomputes both channels after a settings change, without moving to the next top.</summary>
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
        var movies = LoadMovies(config, out _);
        var chosen = PickSelector.ChooseThemes(Today, movies, Disabled(config));
        var state = _store.Get();
        var themed = chosen.Count > 0 ? MakeResult(chosen.Select(c => (c.Pick.Id, c.Items)).ToList(), state.Themed) : null;
        _store.Save(new PickState { Themed = themed, Best = state.Best, BestIndex = state.BestIndex });
        _logger.LogInformation("Jelly Twiggy: themed picks are {Picks}", Describe(themed));
        await SyncChannelAsync(_copies.ThemedName, themed, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateBestCoreAsync(bool rotate, CancellationToken cancellationToken)
    {
        var config = Config;
        var movies = LoadMovies(config, out var libraries);
        var usage = LoadUsage(movies, libraries);
        var state = _store.Get();
        // Without rotation the search starts at the current top, so it stays unless it no longer has enough films.
        var previous = rotate ? state.BestIndex : Math.Max(state.BestIndex, 0) - 1;
        var next = PickSelector.NextTop(previous, movies, usage, Disabled(config));
        var best = next is { } n ? MakeResult([(PickCatalog.Tops[n.Index].Id, n.Items)], state.Best) : null;
        _store.Save(new PickState { Themed = state.Themed, Best = best, BestIndex = next?.Index ?? state.BestIndex });
        _logger.LogInformation("Jelly Twiggy: best pick is {Picks}", Describe(best));
        await SyncChannelAsync(_copies.BestName, best, cancellationToken).ConfigureAwait(false);
    }

    private static string Describe(PickResult? pick) =>
        pick is null ? "none" : string.Join(", ", pick.Groups.Select(g => $"{g.PickId} ({g.ItemIds.Count})"));

    // An unchanged pick keeps its timestamp and posters: the channel cache, folders and copies stay as they are.
    private PickResult MakeResult(IReadOnlyList<(string PickId, IReadOnlyList<Guid> Items)> groups, PickResult? previous)
    {
        if (previous is not null
            && previous.Groups.Count == groups.Count
            && previous.Groups.Zip(groups).All(x => x.First.PickId == x.Second.PickId && x.First.ItemIds.SequenceEqual(x.Second.Items)))
        {
            return new PickResult { Groups = previous.Groups, GeneratedUtc = previous.GeneratedUtc, Date = Today };
        }

        var now = DateTime.UtcNow;
        var culture = _serverConfiguration.Configuration.UICulture;
        return new PickResult
        {
            Groups = groups.Select(g => new PickGroup
            {
                PickId = g.PickId,
                Name = PickNames.Get(g.PickId, culture),
                ItemIds = [.. g.Items],
                PosterId = g.Items.Count > 0 ? g.Items[Random.Shared.Next(g.Items.Count)] : null
            }).ToList(),
            GeneratedUtc = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
            Date = Today
        };
    }

    // Jellyfin compares the channel with its saved copies, adds and deletes them; then the order is set by creation date.
    private async Task SyncChannelAsync(string channelName, PickResult? pick, CancellationToken cancellationToken)
    {
        var channelId = _copies.ChannelId(channelName);
        await _channelManager.GetLatestChannelItemsInternal(new InternalItemsQuery { ChannelIds = [channelId] }, cancellationToken).ConfigureAwait(false);
        if (pick is null || _libraryManager.GetItemById(channelId) is not { } channel)
        {
            return;
        }

        var ranks = pick.AllItemIds()
            .Select((id, rank) => (Key: id.ToString("N"), Rank: rank))
            .ToDictionary(x => x.Key, x => x.Rank, StringComparer.OrdinalIgnoreCase);
        var copies = _libraryManager.GetItemList(new InternalItemsQuery { ChannelIds = [channelId], IsFolder = false, Recursive = true });
        foreach (var copy in copies)
        {
            if (copy.ExternalId is null || !ranks.TryGetValue(copy.ExternalId, out var rank))
            {
                continue;
            }

            // The server sets the date only when it creates a copy; a film that moved needs it set again.
            var createdAt = PickChannel.CreatedAt(pick, rank);
            if (Math.Abs((copy.DateCreated - createdAt).TotalSeconds) >= 0.5)
            {
                copy.DateCreated = createdAt;
                await _libraryManager.UpdateItemAsync(copy, copy.GetParent() ?? channel, ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static HashSet<string> Disabled(PluginConfiguration config) => new(config.DisabledPicks, StringComparer.Ordinal);

    // Only the libraries the admin chose: the channels are shared, so a film there is visible to everyone with channel access.
    private List<MovieFacts> LoadMovies(PluginConfiguration config, out List<BaseItem> libraries)
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
        return _libraryManager.GetItemList(query, libraries)
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
                item.GetProviderId(MetadataProvider.TmdbCollection)))
            .ToList();
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

        var since = DateTime.UtcNow - Week;
        var weekUsers = new Dictionary<Guid, HashSet<Guid>>();
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

            var weekPlays = group.Count(r => !r.Manual && r.EndedUtc >= since);
            if (weekPlays > 0)
            {
                Add(weekUsers, itemId, userId);
                usage.WeekPlays[itemId] = usage.WeekPlays.GetValueOrDefault(itemId) + weekPlays;
            }
        }

        foreach (var user in _userManager.GetUsers())
        {
            foreach (var item in UserMovies(user, libraries, isPlayed: true))
            {
                usage.Touched.Add(item.Id);
                if (logged.Contains((user.Id, item.Id)))
                {
                    continue;
                }

                // History older than the log: PlayCount counts starts, so it adds rewatches only beyond the first.
                Add(viewers, item.Id, user.Id);
                var playCount = _userDataManager.GetUserData(user, item)?.PlayCount ?? 1;
                usage.Rewatches[item.Id] = usage.Rewatches.GetValueOrDefault(item.Id) + Math.Max(playCount - 1, 0);
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

        foreach (var (itemId, users) in weekUsers)
        {
            usage.WeekViewers[itemId] = users.Count;
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
