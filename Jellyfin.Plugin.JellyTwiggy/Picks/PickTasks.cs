using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Daily: today's holiday or franchise for the «Themed» channel. Holiday windows are short, so it runs every day.</summary>
public class ThemedPickTask : IScheduledTask
{
    private readonly PickService _service;
    private readonly IServerConfigurationManager _serverConfiguration;

    /// <summary>Initializes a new instance of the <see cref="ThemedPickTask"/> class.</summary>
    public ThemedPickTask(PickService service, IServerConfigurationManager serverConfiguration)
    {
        _service = service;
        _serverConfiguration = serverConfiguration;
    }

    /// <inheritdoc />
    public string Name => PickNames.Get("taskThemed", _serverConfiguration.Configuration.UICulture);

    /// <inheritdoc />
    public string Key => "JellyTwiggyThemedPick";

    /// <inheritdoc />
    public string Description => PickNames.Get("taskThemedHelp", _serverConfiguration.Configuration.UICulture);

    /// <inheritdoc />
    public string Category => "Jelly Twiggy";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        await _service.RefreshThemedAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(3).Ticks }
    ];
}

/// <summary>Weekly by default: every run moves the «Best» channel to the next top; admins change the schedule in Jellyfin.</summary>
public class BestPickTask : IScheduledTask
{
    private readonly PickService _service;
    private readonly IServerConfigurationManager _serverConfiguration;

    /// <summary>Initializes a new instance of the <see cref="BestPickTask"/> class.</summary>
    public BestPickTask(PickService service, IServerConfigurationManager serverConfiguration)
    {
        _service = service;
        _serverConfiguration = serverConfiguration;
    }

    /// <inheritdoc />
    public string Name => PickNames.Get("taskBest", _serverConfiguration.Configuration.UICulture);

    /// <inheritdoc />
    public string Key => "JellyTwiggyBestPick";

    /// <inheritdoc />
    public string Description => PickNames.Get("taskBestHelp", _serverConfiguration.Configuration.UICulture);

    /// <inheritdoc />
    public string Category => "Jelly Twiggy";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        await _service.UpdateBestAsync(rotate: true, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo { Type = TaskTriggerInfoType.WeeklyTrigger, DayOfWeek = DayOfWeek.Monday, TimeOfDayTicks = TimeSpan.FromHours(3).Ticks }
    ];
}
