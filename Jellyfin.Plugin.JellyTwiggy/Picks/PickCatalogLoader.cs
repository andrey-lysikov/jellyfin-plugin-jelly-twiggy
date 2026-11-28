using System.Text.RegularExpressions;
using Jellyfin.Plugin.JellyTwiggy.Storage;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Result of checking one category of an imported catalog against the real library.</summary>
/// <param name="Id">Category id.</param>
/// <param name="Valid">Whether the category passed validation.</param>
/// <param name="Error">Why it was refused; null when valid.</param>
/// <param name="FilmCount">Films it would pick right now; null when the dry-run was skipped.</param>
/// <param name="IsNew">Not in the built-in catalog.</param>
/// <param name="Overridden">Replaces a built-in category of the same id.</param>
public sealed record CategoryReport(string Id, bool Valid, string? Error, int? FilmCount, bool IsNew, bool Overridden);

/// <summary>Result of checking a whole catalog file.</summary>
/// <param name="Ok">Every category is valid; only then the file can be applied.</param>
/// <param name="Error">Whole-file problem (bad JSON, wrong version); null otherwise.</param>
/// <param name="Categories">Per-category results in file order.</param>
public sealed record CatalogReport(bool Ok, string? Error, List<CategoryReport> Categories);

/// <summary>The effective theme catalog: the built-in one plus an optional custom catalog from the data directory.</summary>
/// <remarks>
/// Custom categories extend or replace built-in ones by id. A broken custom file is ignored with a warning,
/// so the collections always have the built-in catalog to fall back on.
/// </remarks>
public sealed class PickCatalogLoader
{
    private const int MaxCategories = 60;
    private const int MaxPatternLength = 500;

    private static readonly Regex SafeId = new("^[a-z0-9][a-z0-9-]{0,49}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Dictionary<string, Func<int, IEnumerable<DateOnly>>> DateProviders = new(StringComparer.Ordinal)
    {
        ["orthodoxEaster"] = year => [PickCalendar.OrthodoxEaster(year)],
        ["maslenitsa"] = year => [PickCalendar.OrthodoxEaster(year).AddDays(-49)],
        ["chineseNewYear"] = year => PickCalendar.ChineseNewYear(year),
        ["thanksgiving"] = year => [PickCalendar.Thanksgiving(year)],
        ["friday13"] = year => PickCalendar.FridaysThe13th(year)
    };

    // Windows of the built-in themes live in code as lambdas; for export they are mirrored from the embedded JSON.
    private static readonly IReadOnlyDictionary<string, WindowDto> BuiltInWindows = LoadBuiltInWindows();

    private readonly string _customPath;
    private readonly string _backupPath;
    private readonly ILogger<PickCatalogLoader> _logger;
    private readonly Lock _lock = new();
    private IReadOnlyList<PickDefinition>? _themes;
    private Dictionary<string, string>? _customNames;

    /// <summary>Initializes a new instance of the <see cref="PickCatalogLoader"/> class.</summary>
    public PickCatalogLoader(IApplicationPaths applicationPaths, ILogger<PickCatalogLoader> logger)
    {
        _customPath = Path.Combine(JsonFiles.GetDataDirectory(applicationPaths), "catalog.custom.json");
        _backupPath = Path.Combine(JsonFiles.GetDataDirectory(applicationPaths), "catalog.backup.json");
        _logger = logger;
        Current = this;
    }

    /// <summary>Gets the loader registered in DI; set by the constructor. Used by <see cref="PickSelector"/> and <see cref="PickService"/>.</summary>
    public static PickCatalogLoader? Current { get; private set; }

    /// <summary>Gets the themes the picks use now: built-in plus valid custom categories; the built-in ones without a loader.</summary>
    public static IReadOnlyList<PickDefinition> EffectiveThemes => Current?.Themes ?? PickCatalog.Themes;

    /// <summary>Gets the display name of a custom category, or null for built-in ones (they use <see cref="PickNames"/>).</summary>
    public static string? NameOf(string id) => Current?.CustomName(id);

    /// <summary>Gets the themes the picks use now: built-in plus valid custom categories.</summary>
    public IReadOnlyList<PickDefinition> Themes
    {
        get
        {
            lock (_lock)
            {
                return _themes ??= Build();
            }
        }
    }

    /// <summary>Gets the built-in themes only.</summary>
    public IReadOnlyList<PickDefinition> BuiltInThemes => PickCatalog.Themes;

    /// <summary>Gets the display name of a custom category, or null for built-in ones.</summary>
    public string? CustomName(string id)
    {
        lock (_lock)
        {
            _ = Themes; // ensure loaded
            return _customNames?.GetValueOrDefault(id);
        }
    }

    /// <summary>Gets whether a custom catalog is saved.</summary>
    public bool HasCustom => File.Exists(_customPath);

    /// <summary>Gets whether a backup of the previous custom catalog exists for rollback.</summary>
    public bool HasBackup => File.Exists(_backupPath);

    /// <summary>Gets the JSON of the effective catalog (built-in merged with custom) for export.</summary>
    public string ExportJson()
    {
        var merged = BuiltInThemes.Select(ToDto)
            .Concat(CustomCategories().Where(c => BuiltInThemes.All(b => b.Id != c.Id)))
            .ToList();
        return CatalogJson.Serialize(new CatalogDto(CatalogDto.CurrentVersion, merged));
    }

    /// <summary>Checks a catalog JSON without saving it: schema, safety limits and a dry-run against the library.</summary>
    /// <param name="json">The catalog file content.</param>
    /// <param name="filmCounter">Counts films a theme would pick now; null skips the dry-run.</param>
    public CatalogReport Validate(string json, Func<PickDefinition, int>? filmCounter)
    {
        CatalogDto catalog;
        try
        {
            catalog = CatalogJson.Deserialize(json);
        }
        catch (CatalogException ex)
        {
            return new CatalogReport(false, ex.Message, []);
        }

        var reports = catalog.Items.Select(c => Check(c, filmCounter)).ToList();
        if (catalog.Items.Count > MaxCategories)
        {
            return new CatalogReport(false, $"Too many categories ({catalog.Items.Count}); the limit is {MaxCategories}.", reports);
        }

        var duplicate = catalog.Items.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            return new CatalogReport(false, $"Duplicate category id \"{duplicate.Key}\".", reports);
        }

        return new CatalogReport(reports.All(r => r.Valid), null, reports);
    }

    /// <summary>Saves a catalog as the custom one, keeping the previous custom file for rollback. Call only after <see cref="Validate"/>.</summary>
    public void SaveCustom(string json)
    {
        lock (_lock)
        {
            if (File.Exists(_customPath))
            {
                File.Copy(_customPath, _backupPath, overwrite: true);
            }

            var temp = _customPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _customPath, overwrite: true);
            Invalidate();
        }
    }

    /// <summary>Restores the previous custom catalog; false when there is nothing to roll back to.</summary>
    public bool Rollback()
    {
        lock (_lock)
        {
            if (!File.Exists(_backupPath))
            {
                return false;
            }

            File.Copy(_backupPath, _customPath, overwrite: true);
            File.Delete(_backupPath);
            Invalidate();
            return true;
        }
    }

    /// <summary>Removes the custom catalog so only the built-in one applies; false when there was none.</summary>
    public bool ResetToDefault()
    {
        lock (_lock)
        {
            if (!File.Exists(_customPath))
            {
                return false;
            }

            File.Delete(_customPath);
            if (File.Exists(_backupPath))
            {
                File.Delete(_backupPath);
            }

            Invalidate();
            return true;
        }
    }

    private void Invalidate()
    {
        _themes = null;
        _customNames = null;
    }

    private IReadOnlyList<PickDefinition> Build()
    {
        var custom = CustomCategories();
        if (custom.Count == 0)
        {
            _customNames = [];
            return PickCatalog.Themes;
        }

        // Custom replaces built-in categories of the same id and appends new ones after them.
        var replaced = custom.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        _customNames = custom.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToDictionary(c => c.Id, c => c.Name!);
        return PickCatalog.Themes.Where(b => !replaced.Contains(b.Id))
            .Concat(custom.Select(ToDefinition))
            .ToList();
    }

    private List<CategoryDto> CustomCategories()
    {
        if (!File.Exists(_customPath))
        {
            return [];
        }

        try
        {
            var catalog = CatalogJson.Deserialize(File.ReadAllText(_customPath));
            var valid = catalog.Items.Where(c => Check(c, null).Valid).ToList();
            foreach (var broken in catalog.Items.Except(valid))
            {
                _logger.LogWarning("Jelly Twiggy: custom category {Id} is invalid and was skipped", broken.Id);
            }

            return valid;
        }
        catch (Exception ex) when (ex is CatalogException or IOException)
        {
            _logger.LogWarning(ex, "Jelly Twiggy: unreadable custom catalog, using the built-in one");
            return [];
        }
    }

    private static CategoryReport Check(CategoryDto category, Func<PickDefinition, int>? filmCounter)
    {
        var error = ValidateCategory(category);
        int? count = null;
        if (error is null && filmCounter is not null)
        {
            count = filmCounter(ToDefinition(category));
        }

        return new CategoryReport(
            category.Id,
            error is null,
            error,
            count,
            PickCatalog.Themes.All(b => b.Id != category.Id),
            PickCatalog.Themes.Any(b => b.Id == category.Id));
    }

    private static string? ValidateCategory(CategoryDto category)
    {
        if (string.IsNullOrWhiteSpace(category.Id) || !SafeId.IsMatch(category.Id))
        {
            return "Id must be lowercase letters, digits and dashes, up to 50 characters.";
        }

        if (category.Kind is not ("holiday" or "franchise"))
        {
            return "Kind must be \"holiday\" or \"franchise\".";
        }

        if (category.Kind == "holiday" && category.Rule is null)
        {
            return "A holiday needs a rule.";
        }

        if (category.Kind == "franchise" && category.Franchise is null)
        {
            return "A franchise needs collections, studios or originalTitles.";
        }

        if (category.Rule is { } rule)
        {
            foreach (var pattern in new[] { rule.TitlePattern, rule.OverviewPattern })
            {
                if (pattern is null)
                {
                    continue;
                }

                if (pattern.Length > MaxPatternLength)
                {
                    return $"A regex pattern is longer than {MaxPatternLength} characters.";
                }

                try
                {
                    _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException ex)
                {
                    return $"Bad regex: {ex.Message}";
                }
            }
        }

        if (category.Window is { } window)
        {
            if (window.Type is not ("fixed" or "annual" or "computed"))
            {
                return "Window type must be \"fixed\", \"annual\" or \"computed\".";
            }

            if (window.Type == "computed" && (window.DateProvider is null || !DateProviders.ContainsKey(window.DateProvider)))
            {
                return $"Unknown dateProvider \"{window.DateProvider}\"; known: {string.Join(", ", DateProviders.Keys)}.";
            }

            if (window.Type == "annual" && (window.Month is null or < 1 or > 12 || window.Day is null or < 1 or > 31))
            {
                return "An annual window needs month (1-12) and day (1-31).";
            }

            if (window.Type == "fixed" && (window.StartMonth is null or < 1 or > 12 || window.StartDay is null or < 1 or > 31
                || window.EndMonth is null or < 1 or > 12 || window.EndDay is null or < 1 or > 31
                || window.AnchorMonth is null or < 1 or > 12 || window.AnchorDay is null or < 1 or > 31))
            {
                return "A fixed window needs start, end and anchor month/day.";
            }
        }

        return null;
    }

    private static PickDefinition ToDefinition(CategoryDto category) => new(
        category.Id,
        category.Kind == "franchise" ? PickKind.Franchise : PickKind.Holiday,
        category.Window is null ? null : ToWindow(category.Window),
        category.Rule is null ? null : ToRule(category.Rule),
        category.Franchise is null
            ? null
            : new FranchiseRule(
                Set(category.Franchise.Collections),
                Set(category.Franchise.Studios),
                Set(category.Franchise.OriginalTitles)),
        category.Limit ?? 20,
        category.Minimum ?? 6,
        category.Religious,
        category.Season);

    private static CategoryDto ToDto(PickDefinition pick) => new(
        pick.Id,
        pick.Kind == PickKind.Franchise ? "franchise" : "holiday",
        null,
        BuiltInWindows.GetValueOrDefault(pick.Id),
        pick.Rule is null
            ? null
            : new RuleDto(
                [.. pick.Rule.StrongTags],
                [.. pick.Rule.WeakTags],
                pick.Rule.Title?.ToString(),
                pick.Rule.Overview?.ToString(),
                [.. pick.Rule.Genres],
                [.. pick.Rule.Countries],
                pick.Rule.CountriesOnly,
                pick.Rule.RequireStrong,
                pick.Rule.RequireGenre,
                pick.Rule.Threshold == PickSelector.RelevanceThreshold ? null : pick.Rule.Threshold),
        pick.Franchise is null
            ? null
            : new FranchiseDto([.. pick.Franchise.Collections], [.. pick.Franchise.Studios], [.. pick.Franchise.OriginalTitles]),
        pick.Limit == 20 ? null : pick.Limit,
        pick.Minimum == 6 ? null : pick.Minimum,
        pick.Religious,
        pick.Season);

    private static PickWindow ToWindow(WindowDto window) => window.Type switch
    {
        "fixed" => PickWindow.Fixed(window.StartMonth!.Value, window.StartDay!.Value, window.EndMonth!.Value, window.EndDay!.Value,
            window.AnchorMonth!.Value, window.AnchorDay!.Value),
        "annual" => PickWindow.Due(window.Month!.Value, window.Day!.Value, window.Through ?? 0),
        _ => PickWindow.Due(DateProviders[window.DateProvider!], window.Through ?? 0)
    };

    private static ThemeRule ToRule(RuleDto rule) => new(
        Set(rule.StrongTags),
        Set(rule.WeakTags),
        Compile(rule.TitlePattern),
        Compile(rule.OverviewPattern),
        Set(rule.Genres),
        rule.RequireStrong,
        Set(rule.Countries),
        rule.RequireGenre,
        rule.Threshold ?? PickSelector.RelevanceThreshold,
        rule.CountriesOnly);

    private static Regex? Compile(string? pattern) =>
        string.IsNullOrWhiteSpace(pattern)
            ? null
            : new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static IReadOnlySet<string> Set(List<string>? values) =>
        new HashSet<string>(values ?? [], StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, WindowDto> LoadBuiltInWindows()
    {
        var resource = typeof(PickCatalogLoader).Assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("catalog.default.json", StringComparison.Ordinal));
        if (resource is null)
        {
            return new Dictionary<string, WindowDto>();
        }

        using var stream = typeof(PickCatalogLoader).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return CatalogJson.Deserialize(reader.ReadToEnd()).Items
            .Where(c => c.Window is not null)
            .ToDictionary(c => c.Id, c => c.Window!);
    }
}
