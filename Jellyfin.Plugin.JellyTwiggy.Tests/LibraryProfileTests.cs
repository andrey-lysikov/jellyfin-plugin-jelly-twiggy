using Jellyfin.Plugin.JellyTwiggy.Picks;
using Xunit;

namespace Jellyfin.Plugin.JellyTwiggy.Tests;

public class LibraryProfileTests
{
    private static MovieFacts Film(string name, string genre, string tag, string country, int year, string director) =>
        new(Guid.NewGuid(), name, null, null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tag },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { genre },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { country },
            year, 7.0f, null, null)
        { Directors = [director] };

    [Fact]
    public void CountsGroupsAndCoverage()
    {
        var movies = new List<MovieFacts>
        {
            Film("A", "Drama", "space", "US", 1999, "Nolan"),
            Film("B", "Drama", "space", "US", 1997, "Nolan"),
            Film("C", "Comedy", "dog", "FR", 2010, "Other")
        };

        var profile = LibraryProfile.Build(movies, "en");

        Assert.Equal(3, profile.MovieCount);
        Assert.Equal("en", profile.MetadataLanguage);
        Assert.Equal(1.0, profile.TagCoverage);
        Assert.Equal("Drama", profile.Genres[0].Name);
        Assert.Equal(2, profile.Genres[0].Count);
        Assert.Equal("Nolan", profile.Directors[0].Name);
        Assert.Contains(profile.Decades, d => d.Name == "1990s" && d.Count == 2);
        Assert.Contains(profile.Decades, d => d.Name == "2010s" && d.Count == 1);
    }

    [Fact]
    public void EmptyLibraryHasNoCoverage()
    {
        var profile = LibraryProfile.Build([], "ru");
        Assert.Equal(0, profile.MovieCount);
        Assert.Equal(0, profile.TagCoverage);
    }
}
