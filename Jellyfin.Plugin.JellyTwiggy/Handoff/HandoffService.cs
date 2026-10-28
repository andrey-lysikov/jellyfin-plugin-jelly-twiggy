namespace Jellyfin.Plugin.JellyTwiggy.Handoff;

/// <summary>Outcome of a handoff request.</summary>
public enum HandoffStatus
{
    /// <summary>Done.</summary>
    Ok,

    /// <summary>The caller's own session is missing or can't be remote-controlled.</summary>
    NoOwnSession,

    /// <summary>The source no longer plays anything that can be taken over.</summary>
    SourceGone,

    /// <summary>The target device is no longer available.</summary>
    TargetGone
}

/// <summary>Result of a handoff request: status and the other device's name.</summary>
public record HandoffResult(HandoffStatus Status, string? DeviceName = null);

/// <summary>What to play on a target; null fields are left to the receiver.</summary>
public record HandoffPlay(Guid ItemId, long? StartPositionTicks, string? MediaSourceId, int? AudioStreamIndex, int? SubtitleStreamIndex);

/// <summary>"Continue here" and "Play on", in Swiftly's step order. Only the caller's own sessions are touched.</summary>
public class HandoffService
{
    private readonly IHandoffSessions _sessions;
    private readonly Func<DateTime> _now;

    /// <summary>Initializes a new instance of the <see cref="HandoffService"/> class.</summary>
    public HandoffService(IHandoffSessions sessions)
        : this(sessions, () => DateTime.UtcNow)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HandoffService"/> class with a clock, for tests.</summary>
    public HandoffService(IHandoffSessions sessions, Func<DateTime> now)
    {
        _sessions = sessions;
        _now = now;
    }

    /// <summary>Gets the sessions the caller can take over, freshest first.</summary>
    public List<HandoffSession> Sources(Guid userId, string? deviceId) =>
        HandoffRules.TakeoverSources(_sessions.GetSessions(), userId, deviceId, _now());

    /// <summary>Gets the devices the caller can send playback to.</summary>
    public List<HandoffSession> Targets(Guid userId, string? deviceId) =>
        HandoffRules.Targets(_sessions.GetSessions(), userId, deviceId, _now());

    /// <summary>Explains every visible session: why it is or isn't a takeover source and a target.</summary>
    public List<(HandoffSession Session, string? Takeover, string? Target)> Explain(Guid userId, string? deviceId, bool allUsers)
    {
        var now = _now();
        return _sessions.GetSessions()
            .Where(s => allUsers || s.UserId == userId)
            .Select(s => (s, HandoffRules.WhyNotTakeover(s, userId, deviceId, now), HandoffRules.WhyNotTarget(s, userId, deviceId, now)))
            .ToList();
    }

    /// <summary>Stops a session of the caller and starts its video on the caller's own session.</summary>
    public async Task<HandoffResult> TakeOver(Guid userId, string deviceId, string sessionId, CancellationToken cancellationToken)
    {
        var sessions = _sessions.GetSessions();
        var own = OwnSession(sessions, userId, deviceId);
        if (own is null)
        {
            return new HandoffResult(HandoffStatus.NoOwnSession);
        }

        var source = sessions.FirstOrDefault(s => s.Id == sessionId);
        if (source is null || HandoffRules.WhyNotTakeover(source, userId, deviceId, _now()) is not null)
        {
            return new HandoffResult(HandoffStatus.SourceGone);
        }

        // The hint must come first: Swiftly then treats the stop that follows as a takeover.
        if (HandoffRules.IsSwiftly(source.Client))
        {
            await _sessions.SendMessage(own.Id, source.Id, HandoffRules.HintHeader, own.DeviceName ?? string.Empty, HandoffRules.HintTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
        }

        var stoppedAt = await _sessions.StopAndWait(own.Id, source.Id, HandoffRules.StopWait, cancellationToken).ConfigureAwait(false);
        var start = HandoffRules.StartPoint(null, stoppedAt, source.PositionTicks ?? 0);
        var play = HandoffRules.BuildPlay(source.ItemId!.Value, start, source.MediaSourceId, source.AudioStreamIndex, source.SubtitleStreamIndex);
        await _sessions.Play(own.Id, own.Id, play, cancellationToken).ConfigureAwait(false);
        return new HandoffResult(HandoffStatus.Ok, source.DeviceName);
    }

    /// <summary>Sends an item to a target device of the caller.</summary>
    public async Task<HandoffResult> PlayOn(Guid userId, string deviceId, string targetDeviceId, HandoffPlay play, CancellationToken cancellationToken)
    {
        var sessions = _sessions.GetSessions();
        var own = OwnSession(sessions, userId, deviceId);
        var target = ResolveTarget(sessions, userId, deviceId, targetDeviceId);
        if (target is null)
        {
            return new HandoffResult(HandoffStatus.TargetGone);
        }

        var request = HandoffRules.BuildPlay(play.ItemId, play.StartPositionTicks, play.MediaSourceId, play.AudioStreamIndex, play.SubtitleStreamIndex);
        await _sessions.Play(own?.Id ?? string.Empty, target.Id, request, cancellationToken).ConfigureAwait(false);
        return new HandoffResult(HandoffStatus.Ok, target.DeviceName);
    }

    /// <summary>Moves what the caller's own player plays to a target device, then stops it here.</summary>
    public async Task<HandoffResult> MoveFromPlayer(Guid userId, string deviceId, string targetDeviceId, long? positionTicks, CancellationToken cancellationToken)
    {
        var sessions = _sessions.GetSessions();
        var own = OwnSession(sessions, userId, deviceId);
        if (own?.ItemId is not { } itemId || itemId == Guid.Empty)
        {
            return new HandoffResult(HandoffStatus.NoOwnSession);
        }

        var target = ResolveTarget(sessions, userId, deviceId, targetDeviceId);
        if (target is null)
        {
            return new HandoffResult(HandoffStatus.TargetGone);
        }

        // The player's own position is current; the session's lags up to ten seconds.
        var request = HandoffRules.BuildPlay(itemId, positionTicks ?? own.PositionTicks, own.MediaSourceId, own.AudioStreamIndex, own.SubtitleStreamIndex);
        await _sessions.Play(own.Id, target.Id, request, cancellationToken).ConfigureAwait(false);
        await _sessions.Stop(own.Id, own.Id, cancellationToken).ConfigureAwait(false);
        return new HandoffResult(HandoffStatus.Ok, target.DeviceName);
    }

    /// <summary>Moves one of the caller's sessions to another of their devices, keeping position and tracks.</summary>
    public async Task<HandoffResult> Move(Guid userId, string deviceId, string sessionId, string targetDeviceId, CancellationToken cancellationToken)
    {
        var sessions = _sessions.GetSessions();
        var source = sessions.FirstOrDefault(s => s.Id == sessionId);
        if (source is null || HandoffRules.WhyNotTakeover(source, userId, deviceId, _now()) is not null)
        {
            return new HandoffResult(HandoffStatus.SourceGone);
        }

        // Checked before anything stops: a missing target must leave the source playing.
        var target = ResolveTarget(sessions, userId, deviceId, targetDeviceId);
        if (target is null || target.DeviceId == source.DeviceId)
        {
            return new HandoffResult(HandoffStatus.TargetGone);
        }

        var controlling = OwnSession(sessions, userId, deviceId)?.Id ?? string.Empty;
        if (HandoffRules.IsSwiftly(source.Client))
        {
            await _sessions.SendMessage(controlling, source.Id, HandoffRules.HintHeader, target.DeviceName ?? string.Empty, HandoffRules.HintTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
        }

        var stoppedAt = await _sessions.StopAndWait(controlling, source.Id, HandoffRules.StopWait, cancellationToken).ConfigureAwait(false);
        var start = HandoffRules.StartPoint(null, stoppedAt, source.PositionTicks ?? 0);
        var play = HandoffRules.BuildPlay(source.ItemId!.Value, start, source.MediaSourceId, source.AudioStreamIndex, source.SubtitleStreamIndex);
        await _sessions.Play(controlling, target.Id, play, cancellationToken).ConfigureAwait(false);
        return new HandoffResult(HandoffStatus.Ok, target.DeviceName);
    }

    // The caller's own session that can receive commands; its device id comes from the request token.
    private static HandoffSession? OwnSession(IEnumerable<HandoffSession> sessions, Guid userId, string deviceId) =>
        sessions.Where(s => s.UserId == userId && s.SupportsRemoteControl && string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal))
            .MaxBy(s => s.LastActivityDate);

    // The target is stored as a device id: session ids change whenever the app restarts.
    private HandoffSession? ResolveTarget(IEnumerable<HandoffSession> sessions, Guid userId, string deviceId, string targetDeviceId) =>
        HandoffRules.Targets(sessions, userId, deviceId, _now())
            .FirstOrDefault(s => string.Equals(s.DeviceId, targetDeviceId, StringComparison.Ordinal));
}
