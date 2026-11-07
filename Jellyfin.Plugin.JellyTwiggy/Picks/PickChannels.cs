using System.Globalization;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>A channel with a folder per pick (holiday, film series or top); its films are copies of library films.</summary>
// Content is the same for every user: Jellyfin builds the "Latest" row of a channel without a user.
// Folders that left the pick are deleted by Jellyfin with their copies.
public abstract class PickChannel : IChannel, ISupportsLatestMedia
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;

    /// <summary>Initializes a new instance of the <see cref="PickChannel"/> class.</summary>
    protected PickChannel(PickStore store, PickCopies copies, ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager)
    {
        Store = store;
        Copies = copies;
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public string Description => "Jelly Twiggy";

    /// <inheritdoc />
    // Part of Jellyfin's cache key: a new pick must not be served from the 3-hour channel cache.
    public string DataVersion => Current?.GeneratedUtc.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0";

    /// <inheritdoc />
    public string HomePageUrl => "https://github.com/andrey-lysikov/jellyfin-plugin-jelly-twiggy";

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <summary>Gets the saved pick store.</summary>
    protected PickStore Store { get; }

    /// <summary>Gets the channel naming helper.</summary>
    protected PickCopies Copies { get; }

    /// <summary>Gets the current content, or null when nothing fits.</summary>
    protected abstract PickResult? Current { get; }

    /// <summary>Gets the creation date of a copy by its rank over all folders: the first gets the latest, as the "Latest" row sorts by it.</summary>
    public static DateTime CreatedAt(PickResult pick, int rank) => pick.GeneratedUtc.AddSeconds(-rank);

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        MediaTypes = [ChannelMediaType.Video],
        ContentTypes = [ChannelMediaContentType.Movie]
    };

    /// <inheritdoc />
    public bool IsEnabledFor(string userId) =>
        Plugin.Instance?.Configuration is { EnablePicks: true, PickSourceLibraries.Length: > 0 };

    /// <inheritdoc />
    public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var current = Current;
        IReadOnlyList<ChannelItemInfo> items = current is null
            ? []
            : string.IsNullOrEmpty(query.FolderId)
                ? current.Groups.Select(g => Folder(current, g)).ToList()
                : Films(current, current.Groups.FirstOrDefault(g => current.FolderId(g) == query.FolderId));
        return Task.FromResult(new ChannelItemResult { Items = items, TotalRecordCount = items.Count });
    }

    /// <inheritdoc />
    public Task<IEnumerable<ChannelItemInfo>> GetLatestMedia(ChannelLatestMediaSearch request, CancellationToken cancellationToken)
    {
        IEnumerable<ChannelItemInfo> items = Current is { } current ? current.Groups.SelectMany(g => Films(current, g)) : [];
        return Task.FromResult(items);
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken) =>
        Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    // The folder shows the poster of a film picked at random, or of the first one that has a poster.
    private ChannelItemInfo Folder(PickResult pick, PickGroup group) => new()
    {
        Id = pick.FolderId(group),
        Name = group.Name,
        Type = ChannelItemType.Folder,
        FolderType = ChannelFolderType.Container,
        ImageUrl = (group.PosterId is { } posterId ? [posterId, .. group.ItemIds] : group.ItemIds)
            .Select(id => _libraryManager.GetItemById(id)?.PrimaryImagePath)
            .FirstOrDefault(path => !string.IsNullOrEmpty(path)),
        DateCreated = pick.GeneratedUtc,
        DateModified = pick.GeneratedUtc
    };

    private List<ChannelItemInfo> Films(PickResult pick, PickGroup? group)
    {
        var films = new List<ChannelItemInfo>();
        if (group is null)
        {
            return films;
        }

        // Ranks continue from folder to folder, so the row shows the nearest holiday first.
        var rank = pick.Groups.TakeWhile(g => g != group).Sum(g => g.ItemIds.Count);
        foreach (var id in group.ItemIds)
        {
            // A film deleted from the library since the pick was made is skipped.
            if (_libraryManager.GetItemById(id) is { IsFolder: false } film)
            {
                films.Add(Copy(film, CreatedAt(pick, rank)));
            }

            rank++;
        }

        return films;
    }

    // The copy plays the original's files and shows its poster; nothing is copied on disk.
    private ChannelItemInfo Copy(BaseItem film, DateTime createdAt) => new()
    {
        Id = film.Id.ToString("N", CultureInfo.InvariantCulture),
        Name = film.Name,
        OriginalTitle = film.OriginalTitle,
        Overview = film.Overview,
        Genres = [.. film.Genres],
        Studios = [.. film.Studios],
        Tags = [.. film.Tags],
        CommunityRating = film.CommunityRating,
        OfficialRating = film.OfficialRating,
        ProductionYear = film.ProductionYear,
        PremiereDate = film.PremiereDate,
        RunTimeTicks = film.RunTimeTicks,
        ProviderIds = new Dictionary<string, string>(film.ProviderIds, StringComparer.OrdinalIgnoreCase),
        Type = ChannelItemType.Media,
        MediaType = ChannelMediaType.Video,
        ContentType = ChannelMediaContentType.Movie,
        ImageUrl = film.PrimaryImagePath,
        MediaSources = [.. _mediaSourceManager.GetStaticMediaSources(film, false)],
        DateCreated = createdAt,
        DateModified = film.DateModified
    };
}

/// <summary>The «Themed» channel: the current holiday or film series.</summary>
public class ThemedPickChannel : PickChannel
{
    /// <summary>Initializes a new instance of the <see cref="ThemedPickChannel"/> class.</summary>
    public ThemedPickChannel(PickStore store, PickCopies copies, ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager)
        : base(store, copies, libraryManager, mediaSourceManager)
    {
    }

    /// <inheritdoc />
    public override string Name => Copies.ThemedName;

    /// <inheritdoc />
    protected override PickResult? Current => Store.Get().Themed;
}

/// <summary>The «Best» channel: the current server-wide top.</summary>
public class BestPickChannel : PickChannel
{
    /// <summary>Initializes a new instance of the <see cref="BestPickChannel"/> class.</summary>
    public BestPickChannel(PickStore store, PickCopies copies, ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager)
        : base(store, copies, libraryManager, mediaSourceManager)
    {
    }

    /// <inheritdoc />
    public override string Name => Copies.BestName;

    /// <inheritdoc />
    protected override PickResult? Current => Store.Get().Best;
}
