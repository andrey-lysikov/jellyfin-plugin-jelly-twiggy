using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using Jellyfin.Plugin.JellyTwiggy.Media;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyTwiggy.Dashboard;

/// <summary>Counts library items per resolution for the dashboard's library widget.</summary>
public class LibraryCountsService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
    private static readonly BaseItemKind[] Kinds = [BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.MusicVideo];

    private readonly ILibraryManager _libraryManager;
    private readonly Lock _lock = new();
    private Dictionary<string, Dictionary<string, int>>? _cached;
    private DateTime _cachedAt;

    /// <summary>Initializes a new instance of the <see cref="LibraryCountsService"/> class.</summary>
    public LibraryCountsService(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>Gets counts keyed by item kind (Movie, Episode, MusicVideo), then by 4K / 1080p / 720p / SD.</summary>
    public Dictionary<string, Dictionary<string, int>> GetCounts()
    {
        lock (_lock)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAt <= CacheLifetime)
            {
                return _cached;
            }
        }

        // Counted outside the lock: a slow library query must not block other dashboard requests.
        var counts = Kinds.ToDictionary(kind => kind.ToString(), CountKind);
        lock (_lock)
        {
            _cached = counts;
            _cachedAt = DateTime.UtcNow;
            return counts;
        }
    }

    private Dictionary<string, int> CountKind(BaseItemKind kind)
    {
        var counts = new Dictionary<string, int> { ["4K"] = 0, ["1080p"] = 0, ["720p"] = 0, ["SD"] = 0 };
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [kind],
            Recursive = true,
            IsVirtualItem = false
        });

        foreach (var item in items)
        {
            if (Bucket(Resolution.Classify(item.Width, item.Height)) is { } bucket)
            {
                counts[bucket]++;
            }
        }

        return counts;
    }

    // The dashboard groups the poster badge labels into four buckets; unknown size is skipped.
    private static string? Bucket(string? resolution) => resolution switch
    {
        null => null,
        "8K" or "4K" => "4K",
        "1080p" => "1080p",
        "720p" => "720p",
        _ => "SD"
    };
}
