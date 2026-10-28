using Jellyfin.Data.Enums;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.JellyTwiggy.Handoff;

/// <summary>A client session as the handoff rules see it.</summary>
public record HandoffSession(
    string Id,
    Guid UserId,
    string? DeviceId,
    string? DeviceName,
    string? Client,
    bool SupportsRemoteControl,
    bool CanPlayVideo,
    Guid? ItemId,
    BaseItemKind? ItemType,
    long? PositionTicks,
    string? MediaSourceId,
    int? AudioStreamIndex,
    int? SubtitleStreamIndex,
    DateTime LastActivityDate);

/// <summary>"Continue here" and "Play on" with Swiftly Player, in line with its Takeover.fits / startPoint.</summary>
public static class HandoffRules
{
    /// <summary>Hint header Swiftly reads to tell a takeover from a stop. A protocol string: never change it.</summary>
    public const string HintHeader = "Swiftly-Uebernahme";

    /// <summary>Display time of the hint message.</summary>
    public const long HintTimeoutMs = 1000;

    /// <summary>Sessions quiet for longer are treated as gone.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(90);

    /// <summary>How long to wait for the stopped session to report its exact position.</summary>
    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(3);

    private const string SwiftlyPrefix = "Swiftly";
    private const string SwiftlyMusic = "Swiftly Music";

    /// <summary>Whether the client is Swiftly Player (not its music app).</summary>
    public static bool IsSwiftly(string? client) =>
        client is not null && client.StartsWith(SwiftlyPrefix, StringComparison.Ordinal) && client != SwiftlyMusic;

    /// <summary>Why a session can't be taken over, one word per check in Swiftly's order; null when it fits.</summary>
    public static string? WhyNotTakeover(HandoffSession session, Guid userId, string? deviceId, DateTime now)
    {
        if (IsOwnDevice(session, deviceId))
        {
            return "self";
        }

        if (session.UserId != userId)
        {
            return "user";
        }

        if (session.ItemId is null || session.ItemId == Guid.Empty)
        {
            return "idle";
        }

        if (session.ItemType is BaseItemKind.Audio or BaseItemKind.AudioBook or BaseItemKind.MusicAlbum or BaseItemKind.Playlist
            || session.Client == SwiftlyMusic)
        {
            return "audio";
        }

        if (!session.SupportsRemoteControl)
        {
            return "remote";
        }

        if (now - session.LastActivityDate > Freshness)
        {
            return "stale";
        }

        // Swiftly's own checks end here; this plugin offers only Swiftly sessions.
        return IsSwiftly(session.Client) ? null : "client";
    }

    /// <summary>Sessions the caller can take over, freshest first.</summary>
    public static List<HandoffSession> TakeoverSources(IEnumerable<HandoffSession> sessions, Guid userId, string? deviceId, DateTime now) =>
        sessions.Where(s => WhyNotTakeover(s, userId, deviceId, now) is null)
            .OrderByDescending(s => s.LastActivityDate)
            .ToList();

    /// <summary>Why a session can't receive "Play on"; null when it fits. Unlike takeover, it needn't be playing.</summary>
    public static string? WhyNotTarget(HandoffSession session, Guid userId, string? deviceId, DateTime now)
    {
        if (IsOwnDevice(session, deviceId))
        {
            return "self";
        }

        if (session.UserId != userId)
        {
            return "user";
        }

        if (!session.SupportsRemoteControl)
        {
            return "remote";
        }

        if (!session.CanPlayVideo)
        {
            return "video";
        }

        if (now - session.LastActivityDate > Freshness)
        {
            return "stale";
        }

        return IsSwiftly(session.Client) ? null : "client";
    }

    /// <summary>"Play on" targets: one per device (its freshest session), freshest first.</summary>
    public static List<HandoffSession> Targets(IEnumerable<HandoffSession> sessions, Guid userId, string? deviceId, DateTime now) =>
        sessions.Where(s => !string.IsNullOrEmpty(s.DeviceId) && WhyNotTarget(s, userId, deviceId, now) is null)
            .GroupBy(s => s.DeviceId, StringComparer.Ordinal)
            .Select(g => g.MaxBy(s => s.LastActivityDate)!)
            .OrderByDescending(s => s.LastActivityDate)
            .ToList();

    /// <summary>Resume point after a stop: the reported position unless it is missing, unchanged or reset.</summary>
    public static long StartPoint(long? before, long? after, long session)
    {
        // Jellyfin resets the saved position near the start and the end, hence the one-second check.
        if (after is null || after == before)
        {
            return session;
        }

        return after.Value >= session - TimeSpan.TicksPerSecond ? after.Value : session;
    }

    /// <summary>PlayNow with only what to play: the receiver builds its own playback plan.</summary>
    public static PlayRequest BuildPlay(Guid itemId, long? startPositionTicks, string? mediaSourceId, int? audioStreamIndex, int? subtitleStreamIndex) => new()
    {
        ItemIds = [itemId],
        PlayCommand = PlayCommand.PlayNow,
        StartPositionTicks = startPositionTicks is > 0 ? startPositionTicks : null,
        MediaSourceId = string.IsNullOrEmpty(mediaSourceId) ? null! : mediaSourceId,
        AudioStreamIndex = audioStreamIndex,
        SubtitleStreamIndex = subtitleStreamIndex
    };

    private static bool IsOwnDevice(HandoffSession session, string? deviceId) =>
        !string.IsNullOrEmpty(deviceId) && string.Equals(session.DeviceId, deviceId, StringComparison.Ordinal);
}
