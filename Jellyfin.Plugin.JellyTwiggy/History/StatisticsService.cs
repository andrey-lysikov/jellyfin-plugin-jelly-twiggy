using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyTwiggy.History;

/// <summary>Playback statistics for a period.</summary>
public class StatisticsDto
{
    /// <summary>Gets or sets the total number of plays.</summary>
    public int TotalPlays { get; set; }

    /// <summary>Gets or sets the total watch time in seconds.</summary>
    public double TotalSeconds { get; set; }

    /// <summary>Gets or sets users ordered by watch time.</summary>
    public List<UserStatDto> Users { get; set; } = [];

    /// <summary>Gets or sets titles (movies, series, albums) ordered by plays.</summary>
    public List<TitleStatDto> Titles { get; set; } = [];

    /// <summary>Gets or sets the latest plays, newest first.</summary>
    public List<RecentPlayDto> Recent { get; set; } = [];

    /// <summary>Gets or sets the number of plays per play method (DirectPlay, DirectStream, Transcode).</summary>
    public Dictionary<string, int> PlayMethods { get; set; } = [];
}

/// <summary>Per-user statistics.</summary>
public record UserStatDto(Guid UserId, string UserName, int Plays, double Seconds);

/// <summary>Per-title statistics.</summary>
public record TitleStatDto(Guid ItemId, string Name, int Plays, double Seconds, int Users);

/// <summary>One play in the recent feed.</summary>
public record RecentPlayDto(
    Guid UserId,
    string UserName,
    Guid ItemId,
    string ItemName,
    string? SeriesName,
    DateTime EndedUtc,
    double Seconds,
    bool Completed,
    string? Client,
    string? Device,
    string? PlayMethod);

/// <summary>Aggregates the playback log for the admin dashboard.</summary>
public class StatisticsService
{
    private const int TopCount = 10;
    private const int RecentCount = 25;

    private readonly PlaybackHistoryStore _store;
    private readonly IUserManager _userManager;

    /// <summary>Initializes a new instance of the <see cref="StatisticsService"/> class.</summary>
    public StatisticsService(PlaybackHistoryStore store, IUserManager userManager)
    {
        _store = store;
        _userManager = userManager;
    }

    /// <summary>Gets statistics for the last <paramref name="days"/> days; 0 or less means all time.</summary>
    public StatisticsDto GetStatistics(int days)
    {
        var since = days > 0 ? DateTime.UtcNow.AddDays(-days) : DateTime.MinValue;
        // Manual marks are views without watching: they don't count as plays or time.
        var plays = _store.Find(r => !r.Manual && r.EndedUtc >= since);

        // Show current user names; deleted users keep the name they had.
        var names = _userManager.GetUsers().ToDictionary(u => u.Id, u => u.Username);
        string NameOf(PlaybackRecord r) => names.GetValueOrDefault(r.UserId, r.UserName);

        return new StatisticsDto
        {
            TotalPlays = plays.Count,
            TotalSeconds = plays.Sum(r => r.PlayedSeconds),
            Users = plays
                .GroupBy(r => r.UserId)
                .Select(g => new UserStatDto(g.Key, NameOf(g.Last()), g.Count(), g.Sum(r => r.PlayedSeconds)))
                .OrderByDescending(u => u.Seconds)
                .ToList(),
            Titles = plays
                .GroupBy(r => r.TitleId)
                .Select(g => new TitleStatDto(
                    g.Key,
                    g.Last().SeriesName ?? g.Last().ItemName,
                    g.Count(),
                    g.Sum(r => r.PlayedSeconds),
                    g.Select(r => r.UserId).Distinct().Count()))
                .OrderByDescending(t => t.Plays)
                .ThenByDescending(t => t.Seconds)
                .Take(TopCount)
                .ToList(),
            Recent = plays
                .OrderByDescending(r => r.EndedUtc)
                .Take(RecentCount)
                .Select(r => new RecentPlayDto(
                    r.UserId,
                    NameOf(r),
                    r.ItemId,
                    r.ItemName,
                    r.SeriesName,
                    r.EndedUtc,
                    r.PlayedSeconds,
                    r.Completed,
                    r.Client,
                    r.Device,
                    r.PlayMethod))
                .ToList(),
            PlayMethods = plays
                .GroupBy(r => r.PlayMethod ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }
}
