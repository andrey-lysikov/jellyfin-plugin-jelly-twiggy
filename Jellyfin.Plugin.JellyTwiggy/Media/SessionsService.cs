using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.JellyTwiggy.Media;

/// <summary>A client session, playing or idle.</summary>
public class SessionDto
{
    /// <summary>Gets or sets the session id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the user name.</summary>
    public string? UserName { get; set; }

    /// <summary>Gets or sets the user's avatar tag.</summary>
    public string? UserImageTag { get; set; }

    /// <summary>Gets or sets the client app name.</summary>
    public string? Client { get; set; }

    /// <summary>Gets or sets the client app version.</summary>
    public string? ApplicationVersion { get; set; }

    /// <summary>Gets or sets the device name.</summary>
    public string? DeviceName { get; set; }

    /// <summary>Gets or sets the remote address. Admins only.</summary>
    public string? RemoteEndPoint { get; set; }

    /// <summary>Gets or sets the last activity time.</summary>
    public DateTime LastActivityDate { get; set; }

    /// <summary>Gets or sets what is playing, or null for an idle session.</summary>
    public NowPlayingDto? NowPlaying { get; set; }
}

/// <summary>What a session is playing.</summary>
public class NowPlayingDto
{
    /// <summary>Gets or sets the item id.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the item kind.</summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>Gets or sets the item name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the series name for episodes.</summary>
    public string? SeriesName { get; set; }

    /// <summary>Gets or sets the season number.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Gets or sets the album for audio.</summary>
    public string? Album { get; set; }

    /// <summary>Gets or sets the artists for audio.</summary>
    public string? Artists { get; set; }

    /// <summary>Gets or sets the production year.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the id of the item whose primary image to show (series poster for episodes).</summary>
    public Guid ImageItemId { get; set; }

    /// <summary>Gets or sets the runtime.</summary>
    public long? RunTimeTicks { get; set; }

    /// <summary>Gets or sets the playback position.</summary>
    public long? PositionTicks { get; set; }

    /// <summary>Gets or sets a value indicating whether playback is paused.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Gets or sets the play method.</summary>
    public string? PlayMethod { get; set; }

    /// <summary>Gets or sets the transcoding details, when transcoding.</summary>
    public TranscodeDto? Transcode { get; set; }
}

/// <summary>Transcoding details.</summary>
public record TranscodeDto(
    string? VideoCodec,
    string? AudioCodec,
    bool IsVideoDirect,
    bool IsAudioDirect,
    int? Bitrate,
    int? Width,
    int? Height,
    float? Framerate,
    double? CompletionPercentage,
    string? HardwareAcceleration,
    List<string> Reasons);

/// <summary>Lists client sessions.</summary>
public class SessionsService
{
    private readonly ISessionManager _sessionManager;

    /// <summary>Initializes a new instance of the <see cref="SessionsService"/> class.</summary>
    public SessionsService(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    /// <summary>Gets sessions.</summary>
    public List<SessionDto> GetSessions(bool playingOnly, TimeSpan activeWithin, bool includeAddresses)
    {
        var since = DateTime.UtcNow - activeWithin;
        return _sessionManager.Sessions
            .Where(s => s.UserId != Guid.Empty)
            .Where(s => s.NowPlayingItem is not null || (!playingOnly && s.LastActivityDate >= since))
            .Select(s => ToDto(s, includeAddresses))
            .OrderBy(s => s.NowPlaying is null)
            .ThenByDescending(s => s.LastActivityDate)
            .ToList();
    }

    private static SessionDto ToDto(SessionInfo session, bool includeAddresses)
    {
        return new SessionDto
        {
            Id = session.Id,
            UserId = session.UserId,
            UserName = session.UserName,
            UserImageTag = session.UserPrimaryImageTag,
            Client = session.Client,
            ApplicationVersion = session.ApplicationVersion,
            DeviceName = session.DeviceName,
            RemoteEndPoint = includeAddresses ? session.RemoteEndPoint : null,
            LastActivityDate = session.LastActivityDate,
            NowPlaying = session.NowPlayingItem is { } item
                ? new NowPlayingDto
                {
                    ItemId = item.Id,
                    ItemType = item.Type.ToString(),
                    Name = item.Name,
                    SeriesName = item.SeriesName,
                    SeasonNumber = item.ParentIndexNumber,
                    EpisodeNumber = item.IndexNumber,
                    Album = item.Album,
                    Artists = item.Artists is { Count: > 0 } ? string.Join(", ", item.Artists) : null,
                    Year = item.ProductionYear,
                    ImageItemId = item.SeriesId ?? item.AlbumId ?? item.Id,
                    RunTimeTicks = item.RunTimeTicks,
                    PositionTicks = session.PlayState?.PositionTicks,
                    IsPaused = session.PlayState?.IsPaused ?? false,
                    PlayMethod = session.PlayState?.PlayMethod?.ToString(),
                    Transcode = session.PlayState?.PlayMethod == PlayMethod.Transcode && session.TranscodingInfo is { } t
                        ? new TranscodeDto(
                            t.VideoCodec,
                            t.AudioCodec,
                            t.IsVideoDirect,
                            t.IsAudioDirect,
                            t.Bitrate,
                            t.Width,
                            t.Height,
                            t.Framerate,
                            t.CompletionPercentage,
                            t.HardwareAccelerationType?.ToString(),
                            Enum.GetValues<TranscodeReason>()
                                .Where(r => r != 0 && t.TranscodeReasons.HasFlag(r))
                                .Select(r => r.ToString())
                                .ToList())
                        : null
                }
                : null
        };
    }
}
