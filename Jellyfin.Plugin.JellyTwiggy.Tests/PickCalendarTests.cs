using Jellyfin.Plugin.JellyTwiggy.Picks;
using Xunit;

namespace Jellyfin.Plugin.JellyTwiggy.Tests;

public class PickCalendarTests
{
    private static string? FirstActive(int year, int month, int day) =>
        PickCalendar.ActiveThemes(new DateOnly(year, month, day), PickCatalog.Themes).FirstOrDefault()?.Id;

    [Theory]
    [InlineData(2026, 11, 26, "thanksgiving")]
    [InlineData(2026, 11, 27, null)]
    [InlineData(2026, 12, 1, "newyear")]
    [InlineData(2027, 1, 8, "newyear")]
    [InlineData(2027, 1, 9, "epiphany")]
    [InlineData(2027, 1, 19, "epiphany")]
    [InlineData(2026, 2, 13, "friday13")]
    [InlineData(2026, 2, 14, "valentine")]
    [InlineData(2026, 2, 22, "maslenitsa")]
    [InlineData(2026, 2, 23, "defender")]
    [InlineData(2027, 2, 10, "valentine")]
    [InlineData(2027, 3, 10, "maslenitsa")]
    [InlineData(2027, 4, 1, "aprilfools")]
    [InlineData(2026, 4, 12, "cosmonautics")]
    [InlineData(2026, 4, 13, "easter")]
    [InlineData(2027, 4, 30, "mayday")]
    [InlineData(2027, 5, 2, "easter")]
    [InlineData(2027, 5, 4, "starwars")]
    [InlineData(2027, 5, 9, "victory")]
    [InlineData(2027, 5, 20, "childrensday")]
    [InlineData(2027, 6, 12, "russiaday")]
    [InlineData(2027, 6, 20, "music")]
    [InlineData(2027, 6, 25, "summer")]
    [InlineData(2027, 7, 15, "friendship")]
    [InlineData(2027, 8, 5, "harrypotter")]
    [InlineData(2027, 8, 10, "friday13")]
    [InlineData(2027, 8, 30, "school")]
    [InlineData(2027, 9, 10, "tolkien")]
    [InlineData(2027, 10, 1, "animals")]
    [InlineData(2027, 10, 5, "halloween")]
    [InlineData(2027, 10, 15, "halloween")]
    [InlineData(2027, 10, 25, "halloween")]
    [InlineData(2027, 11, 4, "unityday")]
    [InlineData(2026, 11, 10, "friday13")]
    [InlineData(2026, 11, 20, "thanksgiving")]
    public void NearestHolidayNotYetPastWins(int year, int month, int day, string? expected)
    {
        Assert.Equal(expected, FirstActive(year, month, day));
    }

    [Fact]
    public void WindowWrapsOverNewYear()
    {
        var window = PickWindow.Fixed(11, 15, 1, 14, 12, 31);
        Assert.True(PickCalendar.Contains(window, new DateOnly(2027, 1, 2)));
        Assert.True(PickCalendar.Contains(window, new DateOnly(2026, 12, 1)));
        Assert.False(PickCalendar.Contains(window, new DateOnly(2027, 3, 1)));
    }

    [Theory]
    [InlineData(2024, 5, 5)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 12)]
    [InlineData(2027, 5, 2)]
    public void OrthodoxEasterDates(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), PickCalendar.OrthodoxEaster(year));
    }

    [Fact]
    public void MaslenitsaEndsOnForgivenessSunday()
    {
        var maslenitsa = PickCatalog.Themes.Single(t => t.Id == "maslenitsa").Window!;

        Assert.True(PickCalendar.Contains(maslenitsa, new DateOnly(2026, 1, 23)));
        Assert.True(PickCalendar.Contains(maslenitsa, new DateOnly(2026, 2, 22)));
        Assert.False(PickCalendar.Contains(maslenitsa, new DateOnly(2026, 2, 23)));
    }

    [Fact]
    public void ReligiousHolidayGivesWayOnTheSameDay()
    {
        // Orthodox Easter 2026 falls on Cosmonautics Day.
        var themes = PickCalendar.ActiveThemes(new DateOnly(2026, 4, 12), PickCatalog.Themes).Select(t => t.Id).ToList();

        Assert.Equal(new[] { "cosmonautics", "easter" }, themes.Take(2));
    }

    [Fact]
    public void HolidayShowsAMonthAhead()
    {
        var victory = PickCatalog.Themes.Single(t => t.Id == "victory").Window!;

        Assert.False(PickCalendar.Contains(victory, new DateOnly(2027, 4, 8)));
        Assert.True(PickCalendar.Contains(victory, new DateOnly(2027, 4, 9)));
        Assert.True(PickCalendar.Contains(victory, new DateOnly(2027, 5, 9)));
        Assert.False(PickCalendar.Contains(victory, new DateOnly(2027, 5, 10)));
    }

    [Fact]
    public void SummerFillsOnlyDaysWithoutAHoliday()
    {
        var themes = PickCalendar.ActiveThemes(new DateOnly(2027, 7, 15), PickCatalog.Themes).Select(t => t.Id).ToList();

        Assert.Equal("summer", themes[^1]);
    }

    [Fact]
    public void MovingDates()
    {
        Assert.Equal(new[] { new DateOnly(2026, 2, 17) }, PickCalendar.ChineseNewYear(2026));
        Assert.Empty(PickCalendar.ChineseNewYear(2099));
        Assert.Equal(new[] { new DateOnly(2026, 2, 13), new DateOnly(2026, 3, 13), new DateOnly(2026, 11, 13) }, PickCalendar.FridaysThe13th(2026));
        Assert.Equal(new DateOnly(2026, 11, 26), PickCalendar.Thanksgiving(2026));
        Assert.Equal(new DateOnly(2027, 11, 25), PickCalendar.Thanksgiving(2027));
    }

    [Fact]
    public void WeekStartsOnMonday()
    {
        // 2026-11-09 is a Monday: Sunday before it is the previous week.
        Assert.Equal(PickCalendar.WeekIndex(new DateOnly(2026, 11, 9)), PickCalendar.WeekIndex(new DateOnly(2026, 11, 15)));
        Assert.Equal(PickCalendar.WeekIndex(new DateOnly(2026, 11, 9)) - 1, PickCalendar.WeekIndex(new DateOnly(2026, 11, 8)));
    }
}
