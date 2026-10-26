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
public sealed class PlaybackTracker : IHostedService
{
    // Progress reports arrive about every 10 seconds; a longer gap (lost reports, a sleeping client)
    // is not counted as watch time beyond this.
    private const double MaxProgressGapSeconds = 60;

    // Short accidental plays are not worth a history entry.
    private const double MinRecordedSeconds = 30;

    // A client that marks an item played right after finishing it must not count a second view.
    private static readonly TimeSpan ManualMarkDedupWindow = TimeSpan.FromMinutes(10);

    private readonly ISessionManager _sessionManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly PlaybackHistoryStore _store;
    private readonly ILogger<PlaybackTracker> _logger;
    private readonly ConcurrentDictionary<string, ActivePlayback> _active = new();

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
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private static string GetKey(PlaybackProgressEventArgs e) => $"{e.Session?.Id}|{e.Item?.Id}";

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Item is null || e.Users.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        _active[GetKey(e)] = new ActivePlayback
        {
            StartedUtc = now,
            LastReportUtc = now,
            IsPaused = e.IsPaused,
            PositionTicks = e.PlaybackPositionTicks ?? 0,
            PlayMethod = e.Session?.PlayState?.PlayMethod
        };
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Item is null || !_active.TryGetValue(GetKey(e), out var playback))
        {
            // Playback that started before the server (or plugin) did: track it from now on.
            OnPlaybackStart(sender, e);
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

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Item is null || !_active.TryRemove(GetKey(e), out var playback))
        {
            return;
        }

        try
        {
            lock (playback)
            {
                Accumulate(playback);
                playback.PositionTicks = e.PlaybackPositionTicks ?? playback.PositionTicks;
            }

            var completed = e.PlayedToCompletion || ReachedThreshold(playback.PositionTicks, e.Item.RunTimeTicks);
            if (!completed && playback.PlayedSeconds < MinRecordedSeconds)
            {
                return;
            }

            foreach (var user in e.Users)
            {
                var record = CreateRecord(user, e.Item);
                record.StartedUtc = playback.StartedUtc;
                record.EndedUtc = DateTime.UtcNow;
                record.PlayedSeconds = Math.Round(playback.PlayedSeconds);
                record.PositionTicks = playback.PositionTicks;
                record.Completed = completed;
                record.Client = e.ClientName ?? e.Session?.Client;
                record.Device = e.DeviceName ?? e.Session?.DeviceName;
                record.PlayMethod = playback.PlayMethod?.ToString();
                _store.Add(record);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to record playback of {ItemId}", e.Item.Id);
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
            var since = DateTime.UtcNow - ManualMarkDedupWindow;
            var itemId = e.Item.Id;
            if (_store.Find(r => r.UserId == e.UserId && r.ItemId == itemId && r.Completed && r.EndedUtc >= since).Count > 0)
            {
                return;
            }

            var user = _userManager.GetUserById(e.UserId);
            if (user is null)
            {
                return;
            }

            var record = CreateRecord(user, e.Item);
            record.StartedUtc = record.EndedUtc = DateTime.UtcNow;
            record.Completed = true;
            record.Manual = true;
            _store.Add(record);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to record manual mark of {ItemId}", e.Item.Id);
        }
    }

    // Same rule as Jellyfin's "Max resume percentage": past it, the item counts as played.
    private bool ReachedThreshold(long positionTicks, long? runtimeTicks)
    {
        if (runtimeTicks is not > 0)
        {
            return false;
        }

        var threshold = _serverConfiguration.Configuration.MaxResumePct;
        return positionTicks >= runtimeTicks.Value * threshold / 100;
    }

    private static PlaybackRecord CreateRecord(User user, BaseItem item)
    {
        var record = new PlaybackRecord
        {
            UserId = user.Id,
            UserName = user.Username,
            ItemId = item.Id,
            ItemType = item.GetBaseItemKind().ToString(),
            ItemName = item.Name,
            RuntimeTicks = item.RunTimeTicks
        };

        switch (item)
        {
            case Episode episode:
                record.SeriesId = episode.SeriesId == Guid.Empty ? null : episode.SeriesId;
                record.SeriesName = episode.SeriesName;
                record.SeasonId = episode.SeasonId == Guid.Empty ? null : episode.SeasonId;
                break;
            case Audio audio:
                record.AlbumId = audio.AlbumEntity?.Id;
                record.SeriesName = audio.AlbumEntity?.Name;
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
        public DateTime StartedUtc { get; init; }

        public DateTime LastReportUtc { get; set; }

        public bool IsPaused { get; set; }

        public long PositionTicks { get; set; }

        public double PlayedSeconds { get; set; }

        public PlayMethod? PlayMethod { get; set; }
    }
}
