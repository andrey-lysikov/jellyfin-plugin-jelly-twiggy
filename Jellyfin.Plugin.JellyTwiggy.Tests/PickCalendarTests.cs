using Jellyfin.Plugin.JellyTwiggy.Picks;
using Xunit;

namespace Jellyfin.Plugin.JellyTwiggy.Tests;

public class PickCalendarTests
{
    private static string? FirstActive(int year, int month, int day) =>
        PickCalendar.ActiveThemes(new DateOnly(year, month, day), PickCatalog.Themes).FirstOrDefault()?.Id;

    [Theory]
    [InlineData(2026, 11, 14, null)]
    [InlineData(2026, 11, 15, "newyear")]
    [InlineData(2026, 12, 31, "newyear")]
    [InlineData(2027, 1, 14, "newyear")]
    [InlineData(2027, 1, 15, null)]
    [InlineData(2027, 2, 14, "valentine")]
    [InlineData(2027, 2, 15, "defender")]
    [InlineData(2027, 3, 8, "womensday")]
    [InlineData(2027, 4, 1, "aprilfools")]
    [InlineData(2027, 4, 12, "cosmonautics")]
    [InlineData(2027, 5, 4, "starwars")]
    [InlineData(2027, 5, 6, "victory")]
    [InlineData(2027, 6, 1, "summer")]
    [InlineData(2027, 7, 31, "harrypotter")]
    [InlineData(2027, 8, 19, "summer")]
    [InlineData(2027, 9, 1, "school")]
    [InlineData(2027, 9, 22, "tolkien")]
    [InlineData(2027, 10, 5, "bond")]
    [InlineData(2027, 10, 21, "bttf")]
    [InlineData(2027, 10, 31, "halloween")]
    public void NearestWindowWins(int year, int month, int day, string? expected)
    {
        Assert.Equal(expected, FirstActive(year, month, day));
    }

    [Fact]
    public void WindowWrapsOverNewYear()
    {
        var window = new PickWindow(11, 15, 1, 14, 12, 31);
        Assert.True(PickCalendar.Contains(window, new DateOnly(2027, 1, 2)));
        Assert.True(PickCalendar.Contains(window, new DateOnly(2026, 12, 1)));
        Assert.False(PickCalendar.Contains(window, new DateOnly(2027, 3, 1)));
    }

    [Fact]
    public void WeekStartsOnMonday()
    {
        // 2026-11-09 is a Monday: Sunday before it is the previous week.
        Assert.Equal(PickCalendar.WeekIndex(new DateOnly(2026, 11, 9)), PickCalendar.WeekIndex(new DateOnly(2026, 11, 15)));
        Assert.Equal(PickCalendar.WeekIndex(new DateOnly(2026, 11, 9)) - 1, PickCalendar.WeekIndex(new DateOnly(2026, 11, 8)));
    }
}
