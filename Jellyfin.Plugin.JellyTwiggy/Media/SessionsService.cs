using System.Collections.Concurrent;
using Jellyfin.Plugin.JellyTwiggy.Handoff;
using MediaBrowser.Controller.Library;
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

    /// <summary>Gets or sets the device id.</summary>
    public string? DeviceId { get; set; }

    /// <summary>Gets or sets the device name.</summary>
    public string? DeviceName { get; set; }

    /// <summary>Gets or sets the remote address. Admins only.</summary>
    public string? RemoteEndPoint { get; set; }

    /// <summary>Gets or sets the last activity time.</summary>
    public DateTime LastActivityDate { get; set; }

    /// <summary>Gets or sets a value indicating whether the viewer can continue this playback on their own device.</summary>
    public bool CanTakeOver { get; set; }

    /// <summary>Gets or sets what is playing, or null for an idle session.</summary>
    public NowPlayingDto? NowPlaying { get; set; }
}

/// <summary>What a session is playing.</summary>
public class NowPlayingDto
{
    /// <summary>Gets or sets the item id.</summary>
    public Guid ItemId { get; set; }

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

    /// <summary>Gets or sets the bitrate sent to the client: the transcode's, or the file's for direct play.</summary>
    public int? Bitrate { get; set; }

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
    string? HardwareAcceleration);

/// <summary>Lists client sessions.</summary>
public sealed class SessionsService : IDisposable
{
    private readonly ISessionManager _sessionManager;

    // When each session started (or resumed) its current item: playing cards keep their place by it.
    private readonly ConcurrentDictionary<string, (Guid ItemId, DateTime StartedUtc, bool Paused)> _started = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="SessionsService"/> class.</summary>
    public SessionsService(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        _sessionManager.PlaybackStart += OnPlaybackStart;
    }

    /// <inheritdoc />
    public void Dispose() => _sessionManager.PlaybackStart -= OnPlaybackStart;

    /// <summary>Gets sessions: all of them for an admin, only the viewer's own otherwise.</summary>
    public List<SessionDto> GetSessions(Guid viewerId, string? viewerDeviceId, bool handoff, bool isAdmin, bool playingOnly, TimeSpan activeWithin)
    {
        var now = DateTime.UtcNow;
        var since = now - activeWithin;
        var all = _sessionManager.Sessions.ToList();
        ForgetEnded(all);
        return all
            .Where(s => s.UserId != Guid.Empty && (isAdmin || s.UserId == viewerId))
            .Where(s => s.NowPlayingItem is not null || (!playingOnly && s.LastActivityDate >= since))
            .Select(s =>
            {
                var dto = ToDto(s, isAdmin);
                dto.CanTakeOver = handoff && HandoffRules.WhyNotTakeover(JellyfinHandoffSessions.ToHandoff(s), viewerId, viewerDeviceId, now) is null;
                return (Dto: dto, Started: dto.NowPlaying is { } playing ? StartedAt(s.Id, playing.ItemId, playing.IsPaused, now) : DateTime.MaxValue);
            })
            // Playing first, the earliest start on top; a new item or a resume after pause moves the card down.
            .OrderBy(x => x.Started)
            .ThenByDescending(x => x.Dto.LastActivityDate)
            .Select(x => x.Dto)
            .ToList();
    }

    // Sessions playing since before the plugin loaded count from the first time they are seen.
    private DateTime StartedAt(string sessionId, Guid itemId, bool paused, DateTime now)
    {
        var entry = _started.AddOrUpdate(
            sessionId,
            (itemId, now, paused),
            (_, old) => old.ItemId != itemId || (old.Paused && !paused) ? (itemId, now, paused) : old with { Paused = paused });
        return entry.StartedUtc;
    }

    private void ForgetEnded(List<SessionInfo> sessions)
    {
        var alive = sessions.Where(s => s.NowPlayingItem is not null).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _started.Keys.Where(id => !alive.Contains(id)))
        {
            _started.TryRemove(id, out _);
        }
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Session?.Id is { } id && e.Item is { } item)
        {
            _started[id] = (item.Id, DateTime.UtcNow, false);
        }
    }

    private static SessionDto ToDto(SessionInfo session, bool isAdmin)
    {
        return new SessionDto
        {
            Id = session.Id,
            UserId = session.UserId,
            UserName = session.UserName,
            UserImageTag = session.UserPrimaryImageTag,
            Client = session.Client,
            DeviceId = session.DeviceId,
            DeviceName = session.DeviceName,
            RemoteEndPoint = isAdmin ? session.RemoteEndPoint : null,
            LastActivityDate = session.LastActivityDate,
            NowPlaying = session.NowPlayingItem is { } item
                ? new NowPlayingDto
                {
                    ItemId = item.Id,
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
                    Bitrate = session.PlayState?.PlayMethod == PlayMethod.Transcode
                        ? session.TranscodingInfo?.Bitrate
                        : session.FullNowPlayingItem?.TotalBitrate,
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
                            isAdmin ? t.HardwareAccelerationType?.ToString() : null)
                        : null
                }
                : null
        };
    }
}
