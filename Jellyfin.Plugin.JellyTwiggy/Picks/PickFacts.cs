namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>What the selection needs to know about a film, detached from Jellyfin's item model.</summary>
public sealed record MovieFacts(
    Guid Id,
    string Name,
    string? OriginalTitle,
    string? Overview,
    IReadOnlySet<string> Tags,
    IReadOnlySet<string> Genres,
    IReadOnlySet<string> Studios,
    IReadOnlySet<string> Countries,
    int? Year,
    float? CommunityRating,
    float? CriticRating,
    string? CollectionId);

/// <summary>Server-wide use of films: Jellyfin's user data of all users merged with the plugin's playback log.</summary>
public sealed class UsageFacts
{
    /// <summary>Gets the number of distinct users who watched each film to the end.</summary>
    public Dictionary<Guid, int> Viewers { get; } = [];

    /// <summary>Gets the number of views beyond each user's first.</summary>
    public Dictionary<Guid, int> Rewatches { get; } = [];

    /// <summary>Gets the number of users who marked each film as a favorite.</summary>
    public Dictionary<Guid, int> Favorites { get; } = [];

    /// <summary>Gets the distinct users per film over the last 7 days.</summary>
    public Dictionary<Guid, int> WeekViewers { get; } = [];

    /// <summary>Gets the plays per film over the last 7 days.</summary>
    public Dictionary<Guid, int> WeekPlays { get; } = [];

    /// <summary>Gets the films anyone has started, finished or marked.</summary>
    public HashSet<Guid> Touched { get; } = [];
}
