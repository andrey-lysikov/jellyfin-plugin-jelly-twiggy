namespace Jellyfin.Plugin.JellyTwiggy.History;

/// <summary>One playback session of one user, or a manual "mark as played".</summary>
public class PlaybackRecord
{
    /// <summary>Gets or sets the user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the user name at the time of playback.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the played item id.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the item kind, e.g. Movie, Episode, Audio.</summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>Gets or sets the item name.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Gets or sets the series id for episodes.</summary>
    public Guid? SeriesId { get; set; }

    /// <summary>Gets or sets the series name for episodes, or the album name for tracks.</summary>
    public string? SeriesName { get; set; }

    /// <summary>Gets or sets the season id for episodes.</summary>
    public Guid? SeasonId { get; set; }

    /// <summary>Gets or sets the album id for audio tracks.</summary>
    public Guid? AlbumId { get; set; }

    /// <summary>Gets or sets when playback started (UTC).</summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>Gets or sets when playback ended (UTC).</summary>
    public DateTime EndedUtc { get; set; }

    /// <summary>Gets or sets the actual watch time in seconds, excluding pauses.</summary>
    public double PlayedSeconds { get; set; }

    /// <summary>Gets or sets the last playback position.</summary>
    public long PositionTicks { get; set; }

    /// <summary>Gets or sets the item runtime.</summary>
    public long? RuntimeTicks { get; set; }

    /// <summary>Gets or sets a value indicating whether this counts as a completed view.</summary>
    public bool Completed { get; set; }

    /// <summary>Gets or sets a value indicating whether the item was marked as played manually, without playback.</summary>
    public bool Manual { get; set; }

    /// <summary>Gets or sets the client app name.</summary>
    public string? Client { get; set; }

    /// <summary>Gets or sets the device name.</summary>
    public string? Device { get; set; }

    /// <summary>Gets or sets the play method: DirectPlay, DirectStream or Transcode.</summary>
    public string? PlayMethod { get; set; }

    /// <summary>Gets the title id: series for episodes, album for tracks, else the item.</summary>
    public Guid TitleId => SeriesId ?? AlbumId ?? ItemId;
}
