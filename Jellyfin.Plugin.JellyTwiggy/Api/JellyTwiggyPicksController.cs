using Jellyfin.Plugin.JellyTwiggy.Picks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyTwiggy.Api;

/// <summary>Picks for the admin page. Clients get the collections through Jellyfin's standard items API.</summary>
[ApiController]
[Route("JellyTwiggy")]
[Authorize(Policy = Policies.RequiresElevation)]
public class JellyTwiggyPicksController : ControllerBase
{
    private readonly PickStore _store;
    private readonly PickCollections _collections;
    private readonly IServerConfigurationManager _serverConfiguration;

    /// <summary>Initializes a new instance of the <see cref="JellyTwiggyPicksController"/> class.</summary>
    public JellyTwiggyPicksController(
        PickStore store,
        PickCollections collections,
        IServerConfigurationManager serverConfiguration)
    {
        _store = store;
        _collections = collections;
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
        return new PicksDto(
            config is { EnablePicks: true, PickSourceLibraries.Length: > 0 },
            _collections.Name(PickCollectionKind.Themed, state.Themed),
            _collections.Name(PickCollectionKind.Best, state.Best),
            Current(state.Themed),
            Current(state.Best),
            PickCatalog.All.Select(p => new PickInfoDto(p.Id, p.Kind.ToString(), PickNames.Get(p.Id, culture))).ToList());
    }

    private static List<CurrentPickDto> Current(PickResult? pick) =>
        pick is null ? [] : pick.Groups.Select(g => new CurrentPickDto(g.PickId, g.Name, g.ItemIds.Count, pick.GeneratedUtc)).ToList();
}

/// <summary>Picks overview for the admin page.</summary>
// Enabled: the collections are on and have source libraries.
public record PicksDto(bool Enabled, string ThemedCollection, string BestCollection, List<CurrentPickDto> Themed, List<CurrentPickDto> Best, List<PickInfoDto> Catalog);

/// <summary>One group a collection shows now.</summary>
public record CurrentPickDto(string PickId, string Name, int Count, DateTime GeneratedUtc);

/// <summary>One pick of the catalog: Kind is Holiday, Franchise or Top.</summary>
public record PickInfoDto(string Id, string Kind, string Name);
