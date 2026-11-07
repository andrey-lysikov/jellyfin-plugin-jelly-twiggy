namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Date rules of the «Themed» channel.</summary>
public static class PickCalendar
{
    /// <summary>Gets the themes whose window contains the date, the nearest anchor first.</summary>
    public static IReadOnlyList<PickDefinition> ActiveThemes(DateOnly date, IEnumerable<PickDefinition> themes)
    {
        return themes
            .Where(t => t.Window is { } window && Contains(window, date))
            .OrderBy(t => AnchorDistance(t.Window!, date))
            .ToList();
    }

    /// <summary>Gets the week number used to rotate franchises between holidays; weeks start on Monday.</summary>
    public static int WeekIndex(DateOnly date) => date.DayNumber / 7;

    /// <summary>Gets whether a yearly window contains the date.</summary>
    public static bool Contains(PickWindow window, DateOnly date)
    {
        var day = (date.Month * 100) + date.Day;
        var start = (window.StartMonth * 100) + window.StartDay;
        var end = (window.EndMonth * 100) + window.EndDay;
        return start <= end ? day >= start && day <= end : day >= start || day <= end;
    }

    // Days to the anchor in the nearest year, so 2 January is two days after 31 December.
    private static int AnchorDistance(PickWindow window, DateOnly date)
    {
        return Enumerable.Range(date.Year - 1, 3)
            .Select(year => Math.Abs(new DateOnly(year, window.AnchorMonth, window.AnchorDay).DayNumber - date.DayNumber))
            .Min();
    }
}
