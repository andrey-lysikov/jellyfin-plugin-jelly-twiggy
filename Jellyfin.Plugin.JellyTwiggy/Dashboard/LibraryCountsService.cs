using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
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
            if (_cached is null || DateTime.UtcNow - _cachedAt > CacheLifetime)
            {
                _cached = Kinds.ToDictionary(kind => kind.ToString(), CountKind);
                _cachedAt = DateTime.UtcNow;
            }

            return _cached;
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
            if (Classify(item.Width, item.Height) is { } bucket)
            {
                counts[bucket]++;
            }
        }

        return counts;
    }

    // Same thresholds as the poster badges (MediaBadgeService); 8K counts as 4K, unknown size is skipped.
    private static string? Classify(int width, int height)
    {
        if (width <= 0 && height <= 0)
        {
            return null;
        }

        if (width >= 3200 || height >= 2000)
        {
            return "4K";
        }

        if (width >= 1800 || height >= 1000)
        {
            return "1080p";
        }

        return width >= 1200 || height >= 700 ? "720p" : "SD";
    }
}
