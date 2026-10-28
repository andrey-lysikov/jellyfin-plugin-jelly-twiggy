using System.Collections.Concurrent;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.JellyTwiggy.Handoff;

/// <summary>The session commands handoff needs; a seam so the step order can be tested without a server.</summary>
public interface IHandoffSessions
{
    /// <summary>Gets the current sessions.</summary>
    IReadOnlyList<HandoffSession> GetSessions();

    /// <summary>Shows a message on a session.</summary>
    Task SendMessage(string controllingSessionId, string sessionId, string header, string text, long timeoutMs, CancellationToken cancellationToken);

    /// <summary>Stops a session and waits for its stop report; returns the reported position, or null on timeout.</summary>
    Task<long?> StopAndWait(string controllingSessionId, string sessionId, TimeSpan wait, CancellationToken cancellationToken);

    /// <summary>Stops a session without waiting.</summary>
    Task Stop(string controllingSessionId, string sessionId, CancellationToken cancellationToken);

    /// <summary>Sends a play command to a session.</summary>
    Task Play(string controllingSessionId, string sessionId, PlayRequest request, CancellationToken cancellationToken);
}

/// <summary><see cref="IHandoffSessions"/> on top of the server's session manager.</summary>
public sealed class JellyfinHandoffSessions : IHandoffSessions, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<long?>> _stopWaiters = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="JellyfinHandoffSessions"/> class.</summary>
    public JellyfinHandoffSessions(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
    }

    /// <inheritdoc />
    public IReadOnlyList<HandoffSession> GetSessions() => _sessionManager.Sessions.Select(ToHandoff).ToList();

    /// <inheritdoc />
    public Task SendMessage(string controllingSessionId, string sessionId, string header, string text, long timeoutMs, CancellationToken cancellationToken) =>
        _sessionManager.SendMessageCommand(
            controllingSessionId,
            sessionId,
            new MessageCommand { Header = header, Text = text, TimeoutMs = timeoutMs },
            cancellationToken);

    /// <inheritdoc />
    public async Task<long?> StopAndWait(string controllingSessionId, string sessionId, TimeSpan wait, CancellationToken cancellationToken)
    {
        // Registered before the stop goes out, so a fast report is not missed.
        var waiter = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopWaiters[sessionId] = waiter;
        try
        {
            await Stop(controllingSessionId, sessionId, cancellationToken).ConfigureAwait(false);
            var finished = await Task.WhenAny(waiter.Task, Task.Delay(wait, cancellationToken)).ConfigureAwait(false);
            return finished == waiter.Task ? await waiter.Task.ConfigureAwait(false) : null;
        }
        finally
        {
            _stopWaiters.TryRemove(new KeyValuePair<string, TaskCompletionSource<long?>>(sessionId, waiter));
        }
    }

    /// <inheritdoc />
    public Task Stop(string controllingSessionId, string sessionId, CancellationToken cancellationToken) =>
        _sessionManager.SendPlaystateCommand(controllingSessionId, sessionId, new PlaystateRequest { Command = PlaystateCommand.Stop }, cancellationToken);

    /// <inheritdoc />
    public Task Play(string controllingSessionId, string sessionId, PlayRequest request, CancellationToken cancellationToken) =>
        _sessionManager.SendPlayCommand(controllingSessionId, sessionId, request, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _sessionManager.PlaybackStopped -= OnPlaybackStopped;

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Session?.Id is { } id && _stopWaiters.TryGetValue(id, out var waiter))
        {
            waiter.TrySetResult(e.PlaybackPositionTicks);
        }
    }

    /// <summary>Maps a server session to what the handoff rules need.</summary>
    public static HandoffSession ToHandoff(SessionInfo s) => new(
        s.Id,
        s.UserId,
        s.DeviceId,
        s.DeviceName,
        s.Client,
        s.SupportsRemoteControl,
        s.PlayableMediaTypes.Contains(MediaType.Video),
        s.NowPlayingItem?.Id,
        s.NowPlayingItem?.Type,
        s.PlayState?.PositionTicks,
        s.PlayState?.MediaSourceId,
        s.PlayState?.AudioStreamIndex,
        s.PlayState?.SubtitleStreamIndex,
        s.LastActivityDate);
}
