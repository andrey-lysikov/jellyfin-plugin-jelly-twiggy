using Jellyfin.Plugin.JellyTwiggy.Picks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyTwiggy.Api;

/// <summary>Picks and the theme catalog for the admin page. Clients get the collections through Jellyfin's standard items API.</summary>
[ApiController]
[Route("JellyTwiggy")]
[Authorize(Policy = Policies.RequiresElevation)]
public class JellyTwiggyPicksController : ControllerBase
{
    private readonly PickStore _store;
    private readonly PickCollections _collections;
    private readonly PickCatalogLoader _catalog;
    private readonly PickService _service;
    private readonly IServerConfigurationManager _serverConfiguration;

    /// <summary>Initializes a new instance of the <see cref="JellyTwiggyPicksController"/> class.</summary>
    public JellyTwiggyPicksController(
        PickStore store,
        PickCollections collections,
        PickCatalogLoader catalog,
        PickService service,
        IServerConfigurationManager serverConfiguration)
    {
        _store = store;
        _collections = collections;
        _catalog = catalog;
        _service = service;
        _serverConfiguration = serverConfiguration;
    }

    /// <summary>Gets the collection names, what they show now and every pick with its name in the server language.</summary>
    [HttpGet("Picks")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PicksDto> GetJellyTwiggyPicks()
    {
        var culture = _serverConfiguration.Configuration.UICulture;
        var state = _store.Get();
        var config = Plugin.Instance?.Configuration;
        var names = PickNames.Get;
        string NameOf(string id) => _catalog.CustomName(id) ?? names(id, culture);
        return new PicksDto(
            config is { EnablePicks: true, PickSourceLibraries.Length: > 0 },
            _collections.Name(PickCollectionKind.Themed, state.Themed),
            _collections.Name(PickCollectionKind.Best, state.Best),
            Current(state.Themed),
            Current(state.Best),
            _catalog.Themes.Select(p => new PickInfoDto(p.Id, p.Kind.ToString(), NameOf(p.Id))).ToList(),
            _catalog.HasCustom,
            _catalog.HasBackup);
    }

    /// <summary>Gets the statistical profile of the pick libraries: what an LLM (or a person) needs to propose categories.</summary>
    [HttpGet("Picks/Profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<LibraryProfileDto> GetProfile()
    {
        return _service.BuildProfile();
    }

    /// <summary>Downloads the effective catalog (built-in merged with custom) as JSON.</summary>
    [HttpGet("Picks/Catalog")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Catalog()
    {
        var json = _catalog.ExportJson();
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", "jellytwiggy-catalog.json");
    }

    /// <summary>Checks a catalog JSON without saving it: validation and a dry-run count of films per category.</summary>
    [HttpPost("Picks/Catalog/Validate")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogReport>> ValidateCatalog()
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();
        return _catalog.Validate(json, _service.CountFilms);
    }

    /// <summary>Validates and saves a catalog JSON as the custom one, then rebuilds the collections.</summary>
    [HttpPost("Picks/Catalog")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogReport>> ImportCatalog()
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();
        var report = _catalog.Validate(json, _service.CountFilms);
        if (!report.Ok)
        {
            return report;
        }

        _catalog.SaveCustom(json);
        await _service.RebuildAsync(HttpContext.RequestAborted);
        return report;
    }

    /// <summary>Removes the custom catalog, returning to the built-in one, then rebuilds the collections.</summary>
    [HttpDelete("Picks/Catalog")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> ResetCatalog()
    {
        var removed = _catalog.ResetToDefault();
        await _service.RebuildAsync(HttpContext.RequestAborted);
        return new { removed };
    }

    /// <summary>Restores the previous custom catalog, then rebuilds the collections.</summary>
    [HttpPost("Picks/Catalog/Rollback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> RollbackCatalog()
    {
        var restored = _catalog.Rollback();
        if (restored)
        {
            await _service.RebuildAsync(HttpContext.RequestAborted);
        }

        return new { restored };
    }

    private static List<CurrentPickDto> Current(PickResult? pick) =>
        pick is null ? [] : pick.Groups.Select(g => new CurrentPickDto(g.PickId, g.Name, g.ItemIds.Count, pick.GeneratedUtc)).ToList();
}

/// <summary>Picks overview for the admin page.</summary>
// Enabled: the collections are on and have source libraries.
public record PicksDto(
    bool Enabled,
    string ThemedCollection,
    string BestCollection,
    List<CurrentPickDto> Themed,
    List<CurrentPickDto> Best,
    List<PickInfoDto> Catalog,
    bool HasCustomCatalog,
    bool HasCatalogBackup);

/// <summary>One group a collection shows now.</summary>
public record CurrentPickDto(string PickId, string Name, int Count, DateTime GeneratedUtc);

/// <summary>One pick of the catalog: Kind is Holiday, Franchise or Top.</summary>
public record PickInfoDto(string Id, string Kind, string Name);
