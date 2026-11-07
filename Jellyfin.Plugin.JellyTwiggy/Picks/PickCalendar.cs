namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Date rules of the «Themed» channel.</summary>
public static class PickCalendar
{
    /// <summary>How many days before its date a holiday starts to show.</summary>
    public const int LeadDays = 30;

    /// <summary>Gets the themes due on a date: the nearest holiday not yet past first, religious ones after others on the same day, seasons last.</summary>
    public static IReadOnlyList<PickDefinition> ActiveThemes(DateOnly date, IEnumerable<PickDefinition> themes)
    {
        return themes
            .Select(t => (Theme: t, Occurrence: t.Window is { } window ? Find(window, date) : null))
            .Where(x => x.Occurrence is not null)
            .OrderBy(x => x.Theme.Season)
            .ThenBy(x => Math.Max(x.Occurrence!.Value.Anchor.DayNumber - date.DayNumber, 0))
            .ThenBy(x => x.Theme.Religious)
            .Select(x => x.Theme)
            .ToList();
    }

    /// <summary>Gets the week number used to rotate franchises between holidays; weeks start on Monday.</summary>
    public static int WeekIndex(DateOnly date) => date.DayNumber / 7;

    /// <summary>Gets whether a window contains the date.</summary>
    public static bool Contains(PickWindow window, DateOnly date) => Find(window, date) is not null;

    /// <summary>Gets the occurrence of a window that contains the date, if any.</summary>
    public static PickOccurrence? Find(PickWindow window, DateOnly date)
    {
        // Windows cross New Year both ways: the New Year one ends in January, January holidays start in December.
        foreach (var year in new[] { date.Year - 1, date.Year, date.Year + 1 })
        {
            foreach (var occurrence in window.ForYear(year))
            {
                if (occurrence.Start <= date && date <= occurrence.End)
                {
                    return occurrence;
                }
            }
        }

        return null;
    }

    /// <summary>Gets Orthodox Easter (Gregorian date): Meeus' Julian algorithm plus 13 days, valid for 1900–2099.</summary>
    public static DateOnly OrthodoxEaster(int year)
    {
        var a = year % 4;
        var b = year % 7;
        var c = year % 19;
        var d = ((19 * c) + 15) % 30;
        var e = ((2 * a) + (4 * b) - d + 34) % 7;
        var month = (d + e + 114) / 31;
        var day = ((d + e + 114) % 31) + 1;
        return new DateOnly(year, month, day).AddDays(13);
    }

    /// <summary>Gets the Chinese (lunar) New Year of a year; none outside the table.</summary>
    public static IEnumerable<DateOnly> ChineseNewYear(int year)
    {
        // Lunar dates have no simple formula: a table of the years ahead.
        (int Month, int Day)? day = year switch
        {
            2024 => (2, 10), 2025 => (1, 29), 2026 => (2, 17), 2027 => (2, 6), 2028 => (1, 26), 2029 => (2, 13),
            2030 => (2, 3), 2031 => (1, 23), 2032 => (2, 11), 2033 => (1, 31), 2034 => (2, 19), 2035 => (2, 8),
            2036 => (1, 28), 2037 => (2, 15), 2038 => (2, 4), 2039 => (1, 24), 2040 => (2, 12),
            _ => null
        };
        if (day is not { } date)
        {
            return [];
        }

        return [new DateOnly(year, date.Month, date.Day)];
    }

    /// <summary>Gets every Friday that falls on the 13th of a month in a year.</summary>
    public static IEnumerable<DateOnly> FridaysThe13th(int year) =>
        Enumerable.Range(1, 12).Select(month => new DateOnly(year, month, 13)).Where(day => day.DayOfWeek == DayOfWeek.Friday);

    /// <summary>Gets US Thanksgiving: the fourth Thursday of November.</summary>
    public static DateOnly Thanksgiving(int year)
    {
        var first = new DateOnly(year, 11, 1);
        var firstThursday = first.AddDays(((int)DayOfWeek.Thursday - (int)first.DayOfWeek + 7) % 7);
        return firstThursday.AddDays(21);
    }
}
