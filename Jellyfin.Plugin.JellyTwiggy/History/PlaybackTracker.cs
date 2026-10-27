using System.Collections.Concurrent;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.History;

/// <summary>Turns server playback events into <see cref="PlaybackRecord"/>s.</summary>
public sealed class PlaybackTracker : IHostedService, IDisposable
{
    // Progress reports arrive about every 10 seconds; a longer gap (lost reports, a sleeping client)
    // is not counted as watch time beyond this.
    private const double MaxProgressGapSeconds = 60;

    // Short accidental plays are not worth a history entry.
    private const double MinRecordedSeconds = 30;

    // A view needs at least this share of the time it takes to reach the "played" point, so seeking to the end doesn't count.
    private const double MinWatchedShare = 0.5;

    private const int MaxTextLength = 200;

    // A second "completed" for the same user and item within this window is the same view (e.g. a client marking it played).
    private static readonly TimeSpan CompletedDedupWindow = TimeSpan.FromMinutes(10);

    // Playback with no report for this long is treated as stopped (closed tab, lost connection).
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    // A progress report racing a stop must not start a new entry.
    private static readonly TimeSpan StoppedMemory = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly ISessionManager _sessionManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly PlaybackHistoryStore _store;
    private readonly ILogger<PlaybackTracker> _logger;
    private readonly ConcurrentDictionary<string, ActivePlayback> _active = new();
    private readonly ConcurrentDictionary<string, DateTime> _recentlyStopped = new();
    private readonly Lock _recordLock = new();
    private Timer? _sweepTimer;

    /// <summary>Initializes a new instance of the <see cref="PlaybackTracker"/> class.</summary>
    public PlaybackTracker(
        ISessionManager sessionManager,
        IUserDataManager userDataManager,
        IUserManager userManager,
        IServerConfigurationManager serverConfiguration,
        PlaybackHistoryStore store,
        ILogger<PlaybackTracker> logger)
    {
        _sessionManager = sessionManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
        _serverConfiguration = serverConfiguration;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionEnded += OnSessionEnded;
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _sweepTimer = new Timer(_ => Sweep(), null, SweepInterval, SweepInterval);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionEnded -= OnSessionEnded;
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _sweepTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _sweepTimer?.Dispose();

    private static string GetKey(PlaybackProgressEventArgs e) => $"{e.Session?.Id}|{e.Item?.Id}";

    private static string? Truncate(string? text) => text is null || text.Length <= MaxTextLength ? text : text[..MaxTextLength];

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            Begin(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to track playback start");
        }
    }

    private void Begin(PlaybackProgressEventArgs e)
    {
        if (e.Item is null || e.Users.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var key = GetKey(e);
        _recentlyStopped.TryRemove(key, out _);
        _active[key] = new ActivePlayback
        {
            Item = e.Item,
            Users = [.. e.Users],
            Client = e.ClientName ?? e.Session?.Client,
            Device = e.DeviceName ?? e.Session?.DeviceName,
            StartedUtc = now,
            LastReportUtc = now,
            IsPaused = e.IsPaused,
            PositionTicks = e.PlaybackPositionTicks ?? 0,
            PlayMethod = e.Session?.PlayState?.PlayMethod
        };
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            if (e.Item is null)
            {
                return;
            }

            var key = GetKey(e);
            if (!_active.TryGetValue(key, out var playback))
            {
                // Playback that started before the server (or plugin) did is tracked from now on,
                // unless this report trails a stop that just happened.
                if (!_recentlyStopped.ContainsKey(key))
                {
                    Begin(e);
                }

                return;
            }

            lock (playback)
            {
                Accumulate(playback);
                playback.IsPaused = e.IsPaused;
                playback.PositionTicks = e.PlaybackPositionTicks ?? playback.PositionTicks;
                // Remember a transcode even if the session switches to direct play later.
                if (playback.PlayMethod != PlayMethod.Transcode)
                {
                    playback.PlayMethod = e.Session?.PlayState?.PlayMethod ?? playback.PlayMethod;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to track playback progress");
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        try
        {
            if (e.Item is not null)
            {
                Finish(GetKey(e), e.PlaybackPositionTicks, accumulate: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to record playback of {ItemId}", e.Item?.Id);
        }
    }

    // A closed tab or dropped connection ends the session without a stop event.
    private void OnSessionEnded(object? sender, SessionEventArgs e)
    {
        try
        {
            var prefix = $"{e.SessionInfo?.Id}|";
            foreach (var key in _active.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                Finish(key, null, accumulate: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to record playback of an ended session");
        }
    }

    private void Sweep()
    {
        try
        {
            var now = DateTime.UtcNow;
            foreach (var (key, playback) in _active.ToArray())
            {
                if (now - playback.LastReportUtc > StaleAfter)
                {
                    // No reports for minutes: the time since the last one is not watch time.
                    Finish(key, null, accumulate: false);
                }
            }

            foreach (var (key, stoppedAt) in _recentlyStopped.ToArray())
            {
                if (now - stoppedAt > StoppedMemory)
                {
                    _recentlyStopped.TryRemove(key, out _);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to sweep stale playback");
        }
    }

    private void Finish(string key, long? positionTicks, bool accumulate)
    {
        if (!_active.TryRemove(key, out var playback))
        {
            return;
        }

        _recentlyStopped[key] = DateTime.UtcNow;
        lock (playback)
        {
            if (accumulate)
            {
                Accumulate(playback);
            }

            playback.PositionTicks = positionTicks ?? playback.PositionTicks;
        }

        var completed = IsCompleted(playback.PositionTicks, playback.PlayedSeconds, playback.Item.RunTimeTicks);
        if (!completed && playback.PlayedSeconds < MinRecordedSeconds)
        {
            return;
        }

        foreach (var user in playback.Users)
        {
            var record = CreateRecord(user, playback.Item);
            record.StartedUtc = playback.StartedUtc;
            record.EndedUtc = DateTime.UtcNow;
            record.PlayedSeconds = Math.Round(playback.PlayedSeconds);
            record.PositionTicks = playback.PositionTicks;
            record.Client = Truncate(playback.Client);
            record.Device = Truncate(playback.Device);
            record.PlayMethod = playback.PlayMethod?.ToString();
            AddRecord(record, completed);
        }
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        if (e.SaveReason != UserDataSaveReason.TogglePlayed || !e.UserData.Played || e.Item is null || e.Item.IsFolder)
        {
            return;
        }

        try
        {
            var user = _userManager.GetUserById(e.UserId);
            if (user is null)
            {
                return;
            }

            var record = CreateRecord(user, e.Item);
            record.StartedUtc = record.EndedUtc = DateTime.UtcNow;
            record.Manual = true;
            AddRecord(record, completed: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to record manual mark of {ItemId}", e.Item.Id);
        }
    }

    // The single place that decides whether a record counts as a view; dedup and write are one step.
    private void AddRecord(PlaybackRecord record, bool completed)
    {
        lock (_recordLock)
        {
            var since = record.EndedUtc - CompletedDedupWindow;
            var repeat = completed && _store.ForUserItem(record.UserId, record.ItemId).Any(r => r.Completed && r.EndedUtc >= since);
            if (repeat && record.Manual)
            {
                return;
            }

            // A repeat that was actually watched keeps its watch time but is not a second view.
            record.Completed = completed && !repeat;
            _store.Add(record);
        }
    }

    // Same point as Jellyfin's "Max resume percentage", plus enough real watch time to get there.
    private bool IsCompleted(long positionTicks, double playedSeconds, long? runtimeTicks)
    {
        if (runtimeTicks is not > 0)
        {
            return false;
        }

        var threshold = _serverConfiguration.Configuration.MaxResumePct;
        var thresholdTicks = runtimeTicks.Value * threshold / 100;
        var neededSeconds = TimeSpan.FromTicks(thresholdTicks).TotalSeconds * MinWatchedShare;
        return positionTicks >= thresholdTicks && playedSeconds >= neededSeconds;
    }

    private static PlaybackRecord CreateRecord(User user, BaseItem item)
    {
        var record = new PlaybackRecord
        {
            UserId = user.Id,
            UserName = Truncate(user.Username) ?? string.Empty,
            ItemId = item.Id,
            ItemType = item.GetBaseItemKind().ToString(),
            ItemName = Truncate(item.Name) ?? string.Empty,
            RuntimeTicks = item.RunTimeTicks
        };

        switch (item)
        {
            case Episode episode:
                record.SeriesId = episode.SeriesId == Guid.Empty ? null : episode.SeriesId;
                record.SeriesName = Truncate(episode.SeriesName);
                record.SeasonId = episode.SeasonId == Guid.Empty ? null : episode.SeasonId;
                break;
            case Audio audio:
                record.AlbumId = audio.AlbumEntity?.Id;
                record.SeriesName = Truncate(audio.AlbumEntity?.Name);
                break;
        }

        return record;
    }

    private static void Accumulate(ActivePlayback playback)
    {
        var now = DateTime.UtcNow;
        if (!playback.IsPaused)
        {
            playback.PlayedSeconds += Math.Min((now - playback.LastReportUtc).TotalSeconds, MaxProgressGapSeconds);
        }

        playback.LastReportUtc = now;
    }

    private sealed class ActivePlayback
    {
        public required BaseItem Item { get; init; }

        public required List<User> Users { get; init; }

        public string? Client { get; init; }

        public string? Device { get; init; }

        public DateTime StartedUtc { get; init; }

        public DateTime LastReportUtc { get; set; }

        public bool IsPaused { get; set; }

        public long PositionTicks { get; set; }

        public double PlayedSeconds { get; set; }

        public PlayMethod? PlayMethod { get; set; }
    }
}
