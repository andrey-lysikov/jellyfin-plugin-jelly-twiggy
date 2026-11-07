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
    string? CollectionId)
{
    /// <summary>Gets or sets the directors; loaded only for the «Best» tops.</summary>
    public IReadOnlyList<string> Directors { get; init; } = [];

    /// <summary>Gets or sets the first five actors in the credits; loaded only for the «Best» tops.</summary>
    public IReadOnlyList<string> Actors { get; init; } = [];
}

/// <summary>What a top needs beyond the films: today's date and how many times it has come round.</summary>
// Round picks the person of a "director/star of the week" top; each turn of the top moves to the next one.
public sealed record TopContext(DateOnly Today, int Round);

/// <summary>A top's films and, for a person top, whose films they are.</summary>
public sealed record TopChoice(IReadOnlyList<Guid> Items, string? Subject);

/// <summary>Server-wide use of films: Jellyfin's user data of all users merged with the plugin's playback log.</summary>
public sealed class UsageFacts
{
    /// <summary>Gets the number of distinct users who watched each film to the end.</summary>
    public Dictionary<Guid, int> Viewers { get; } = [];

    /// <summary>Gets the number of views beyond each user's first.</summary>
    public Dictionary<Guid, int> Rewatches { get; } = [];

    /// <summary>Gets the number of users who marked each film as a favorite.</summary>
    public Dictionary<Guid, int> Favorites { get; } = [];

    /// <summary>Gets when anyone on the server last watched each film (UTC).</summary>
    public Dictionary<Guid, DateTime> LastWatched { get; } = [];

    /// <summary>Gets the films anyone has started, finished or marked.</summary>
    public HashSet<Guid> Touched { get; } = [];
}
