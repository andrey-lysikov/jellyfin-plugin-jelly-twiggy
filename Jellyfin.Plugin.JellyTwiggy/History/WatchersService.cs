using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyTwiggy.History;

/// <summary>A user who watched an item.</summary>
public class WatcherDto
{
    /// <summary>Gets or sets the user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the user name.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the avatar cache tag, or null if the user has no avatar.</summary>
    public string? ImageTag { get; set; }

    /// <summary>Gets or sets how many times the item was watched (playable items only).</summary>
    public int PlayCount { get; set; }

    /// <summary>Gets or sets when the item was last watched.</summary>
    public DateTime? LastWatched { get; set; }

    /// <summary>Gets or sets the resume position in percent if the user stopped part-way.</summary>
    public int? ProgressPercent { get; set; }

    /// <summary>Gets or sets the number of watched children (episodes or tracks) for series, seasons and albums.</summary>
    public int? WatchedCount { get; set; }

    /// <summary>Gets or sets the total number of children for series, seasons and albums.</summary>
    public int? TotalCount { get; set; }
}

/// <summary>Who watched an item, how many times and when.</summary>
// Count: completed views from our log, else Jellyfin's PlayCount; last date: the latest of both.
public class WatchersService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly PlaybackHistoryStore _store;

    /// <summary>Initializes a new instance of the <see cref="WatchersService"/> class.</summary>
    public WatchersService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        PlaybackHistoryStore store)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _store = store;
    }

    /// <summary>Gets everyone who watched an item.</summary>
    public List<WatcherDto>? GetWatchers(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return null;
        }

        var childKind = item.GetBaseItemKind() switch
        {
            BaseItemKind.Series or BaseItemKind.Season => BaseItemKind.Episode,
            BaseItemKind.MusicAlbum => BaseItemKind.Audio,
            _ => (BaseItemKind?)null
        };

        var watchers = new List<WatcherDto>();
        foreach (var user in _userManager.GetUsers())
        {
            var watcher = childKind is null
                ? GetItemWatcher(user, item)
                : GetContainerWatcher(user, item, childKind.Value);
            if (watcher is not null)
            {
                watcher.UserId = user.Id;
                watcher.UserName = user.Username;
                watcher.ImageTag = user.ProfileImage is null
                    ? null
                    : user.ProfileImage.LastModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                watchers.Add(watcher);
            }
        }

        return watchers.OrderByDescending(w => w.LastWatched ?? DateTime.MinValue).ToList();
    }

    private WatcherDto? GetItemWatcher(User user, BaseItem item)
    {
        var userId = user.Id;
        var itemId = item.Id;
        var records = _store.Find(r => r.UserId == userId && r.ItemId == itemId);
        var data = _userDataManager.GetUserData(user, item);

        var loggedViews = records.Count(r => r.Completed);
        var playCount = loggedViews > 0 ? loggedViews : data?.PlayCount ?? 0;
        if (playCount == 0 && data?.Played == true)
        {
            playCount = 1;
        }

        var lastWatched = Latest(records.Count > 0 ? records.Max(r => r.EndedUtc) : null, data?.LastPlayedDate);

        int? progress = null;
        if (data is { PlaybackPositionTicks: > 0 } && item.RunTimeTicks is > 0)
        {
            progress = (int)Math.Clamp(data.PlaybackPositionTicks * 100 / item.RunTimeTicks.Value, 1, 99);
        }

        if (playCount == 0 && progress is null)
        {
            return null;
        }

        return new WatcherDto { PlayCount = playCount, LastWatched = lastWatched, ProgressPercent = progress };
    }

    private WatcherDto? GetContainerWatcher(User user, BaseItem container, BaseItemKind childKind)
    {
        var watched = _libraryManager.GetCount(ChildQuery(container, childKind, user, isPlayed: true));
        var inProgress = watched == 0 && _libraryManager.GetCount(ChildQuery(container, childKind, user, isResumable: true)) > 0;
        if (watched == 0 && !inProgress)
        {
            return null;
        }

        var containerId = container.Id;
        var userId = user.Id;
        var records = _store.Find(r => r.UserId == userId
            && (r.SeriesId == containerId || r.SeasonId == containerId || r.AlbumId == containerId));
        DateTime? lastFromLog = records.Count > 0 ? records.Max(r => r.EndedUtc) : null;

        // The most recently played child gives the date for history older than the log.
        var lastQuery = ChildQuery(container, childKind, user);
        lastQuery.OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)];
        lastQuery.Limit = 1;
        var lastChild = _libraryManager.GetItemList(lastQuery).FirstOrDefault();
        var lastFromData = lastChild is null ? null : _userDataManager.GetUserData(user, lastChild)?.LastPlayedDate;

        return new WatcherDto
        {
            WatchedCount = watched,
            TotalCount = _libraryManager.GetCount(ChildQuery(container, childKind, null)),
            LastWatched = Latest(lastFromLog, lastFromData)
        };
    }

    private static InternalItemsQuery ChildQuery(BaseItem container, BaseItemKind childKind, User? user, bool? isPlayed = null, bool? isResumable = null)
    {
        var query = user is null ? new InternalItemsQuery() : new InternalItemsQuery(user);
        query.AncestorIds = [container.Id];
        query.IncludeItemTypes = [childKind];
        query.Recursive = true;
        query.IsVirtualItem = false;
        query.IsPlayed = isPlayed;
        query.IsResumable = isResumable;
        return query;
    }

    private static DateTime? Latest(DateTime? a, DateTime? b)
    {
        if (a is null)
        {
            return b;
        }

        return b is null || a > b ? a : b;
    }
}
