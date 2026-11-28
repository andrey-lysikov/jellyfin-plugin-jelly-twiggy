using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyTwiggy.Storage;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>JSON form of the pick catalog: the shape admins export, edit (or generate) and import.</summary>
/// <param name="Version">Catalog format version; unknown fields are ignored, unknown versions are refused.</param>
/// <param name="Categories">Themes of the «Themed» collection in calendar and rotation order.</param>
public sealed record CatalogDto(int Version, List<CategoryDto>? Categories)
{
    /// <summary>The only catalog format version this build reads.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Gets the categories, never null.</summary>
    public List<CategoryDto> Items => Categories ?? [];
}

/// <summary>One theme or franchise of the catalog. Kind is <c>holiday</c> or <c>franchise</c>; tops stay in code.</summary>
/// <param name="Name">Display name for custom categories; built-in ones are named by <see cref="PickNames"/> per server language.</param>
public sealed record CategoryDto(
    string Id,
    string Kind,
    string? Name = null,
    WindowDto? Window = null,
    RuleDto? Rule = null,
    FranchiseDto? Franchise = null,
    int? Limit = null,
    int? Minimum = null,
    bool Religious = false,
    bool Season = false);

/// <summary>A date window. Type is <c>fixed</c> (a season's days), <c>annual</c> (month/day with <see cref="Through"/>)
/// or <c>computed</c> (a named date provider: orthodoxEaster, maslenitsa, chineseNewYear, thanksgiving, friday13).</summary>
public sealed record WindowDto(
    string Type,
    int? Month = null,
    int? Day = null,
    int? Through = null,
    int? StartMonth = null,
    int? StartDay = null,
    int? EndMonth = null,
    int? EndDay = null,
    int? AnchorMonth = null,
    int? AnchorDay = null,
    string? DateProvider = null);

/// <summary>How a holiday matches a film; mirrors <see cref="ThemeRule"/>. Patterns are regex, all other lists are plain names.</summary>
public sealed record RuleDto(
    List<string>? StrongTags = null,
    List<string>? WeakTags = null,
    string? TitlePattern = null,
    string? OverviewPattern = null,
    List<string>? Genres = null,
    List<string>? Countries = null,
    bool CountriesOnly = false,
    bool RequireStrong = false,
    bool RequireGenre = false,
    int? Threshold = null);

/// <summary>Films of a franchise: TMDb collections, studios or exact original titles.</summary>
public sealed record FranchiseDto(
    List<string>? Collections = null,
    List<string>? Studios = null,
    List<string>? OriginalTitles = null);

/// <summary>Reads and writes the catalog JSON with the plugin's shared serializer settings.</summary>
public static class CatalogJson
{
    private static readonly JsonSerializerOptions Options = new(JsonFiles.Options)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Serializes the catalog for export or saving.</summary>
    public static string Serialize(CatalogDto catalog) => JsonSerializer.Serialize(catalog, Options);

    /// <summary>Parses catalog JSON; throws <see cref="CatalogException"/> with a readable message on bad input.</summary>
    public static CatalogDto Deserialize(string json)
    {
        CatalogDto? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<CatalogDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new CatalogException($"Invalid JSON: {ex.Message}");
        }

        if (catalog is null)
        {
            throw new CatalogException("The file is empty or not a catalog.");
        }

        if (catalog.Version != CatalogDto.CurrentVersion)
        {
            throw new CatalogException($"Unsupported catalog version {catalog.Version}; this plugin reads version {CatalogDto.CurrentVersion}.");
        }

        return catalog;
    }
}

/// <summary>A catalog file that cannot be used; the message is shown to the admin as is.</summary>
public sealed class CatalogException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CatalogException"/> class.</summary>
    public CatalogException(string message)
        : base(message)
    {
    }
}
