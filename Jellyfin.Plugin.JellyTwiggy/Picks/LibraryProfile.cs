namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>A compact statistical profile of the pick libraries: what an LLM (or a person) needs to propose categories.</summary>
/// <param name="ProfileVersion">Profile format version; bump when the shape changes.</param>
/// <param name="MetadataLanguage">The server's metadata language: genres, countries and patterns should use it.</param>
/// <param name="MovieCount">Films in the pick source libraries.</param>
/// <param name="TagCoverage">Share of films (0–1) that have any tags; low values mean rules should lean on genres, countries and studios instead of tags.</param>
public sealed record LibraryProfileDto(
    int ProfileVersion,
    string? MetadataLanguage,
    int MovieCount,
    double TagCoverage,
    List<CountDto> Genres,
    List<CountDto> Tags,
    List<CountDto> Studios,
    List<CountDto> Countries,
    List<CountDto> Directors,
    List<CountDto> Decades,
    Dictionary<string, List<string>> Samples)
{
    /// <summary>The only profile format version this build writes.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>One name and how many films have it.</summary>
public record CountDto(string Name, int Count);

/// <summary>Builds the profile from film facts. Pure, so it is unit tested.</summary>
public static class LibraryProfile
{
    /// <summary>Builds the profile: top-N lists plus a few sample titles per big group, for regex writing.</summary>
    /// <param name="movies">Films of the pick source libraries.</param>
    /// <param name="metadataLanguage">The server's metadata language.</param>
    /// <param name="top">How many entries each list keeps.</param>
    /// <param name="sampleGroups">How many top studios and countries get sample titles.</param>
    /// <param name="samplesPerGroup">Sample titles per group.</param>
    public static LibraryProfileDto Build(
        IReadOnlyList<MovieFacts> movies,
        string? metadataLanguage,
        int top = 25,
        int sampleGroups = 8,
        int samplesPerGroup = 4)
    {
        List<CountDto> Count(IEnumerable<string> names, int limit) => names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CountDto(g.First(), g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        Dictionary<string, List<string>> Samples(IEnumerable<(string Group, MovieFacts Movie)> pairs, string prefix)
        {
            return pairs
                .GroupBy(p => p.Group, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(sampleGroups)
                .ToDictionary(
                    g => prefix + g.Key,
                    g => g.Select(p => p.Movie.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.Ordinal)
                        .Take(samplesPerGroup)
                        .ToList(),
                    StringComparer.Ordinal);
        }

        var samples = Samples(movies.SelectMany(m => m.Studios.Select(s => (s, m))), "studio:")
            .Concat(Samples(movies.SelectMany(m => m.Countries.Select(c => (c, m))), "country:").AsEnumerable())
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        return new LibraryProfileDto(
            LibraryProfileDto.CurrentVersion,
            metadataLanguage,
            movies.Count,
            movies.Count == 0 ? 0 : Math.Round(movies.Count(m => m.Tags.Count > 0) / (double)movies.Count, 2),
            Count(movies.SelectMany(m => m.Genres), top),
            Count(movies.SelectMany(m => m.Tags), Math.Max(top, 60)),
            Count(movies.SelectMany(m => m.Studios), top),
            Count(movies.SelectMany(m => m.Countries), top),
            Count(movies.SelectMany(m => m.Directors), 20),
            Count(movies.Where(m => m.Year is not null).Select(m => $"{m.Year / 10 * 10}s"), 20),
            samples);
    }
}
