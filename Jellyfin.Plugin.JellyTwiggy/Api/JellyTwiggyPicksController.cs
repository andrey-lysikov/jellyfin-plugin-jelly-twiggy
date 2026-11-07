using Jellyfin.Plugin.JellyTwiggy.Picks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyTwiggy.Api;

/// <summary>Picks for the admin page. Clients get the picks through Jellyfin's standard channel API.</summary>
[ApiController]
[Route("JellyTwiggy")]
[Authorize(Policy = Policies.RequiresElevation)]
public class JellyTwiggyPicksController : ControllerBase
{
    private readonly PickStore _store;
    private readonly PickCopies _copies;
    private readonly IServerConfigurationManager _serverConfiguration;

    /// <summary>Initializes a new instance of the <see cref="JellyTwiggyPicksController"/> class.</summary>
    public JellyTwiggyPicksController(PickStore store, PickCopies copies, IServerConfigurationManager serverConfiguration)
    {
        _store = store;
        _copies = copies;
        _serverConfiguration = serverConfiguration;
    }

    /// <summary>Gets the channel names, what they show now and every pick with its name in the server language.</summary>
    [HttpGet("Picks")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PicksDto> GetJellyTwiggyPicks()
    {
        var culture = _serverConfiguration.Configuration.UICulture;
        var state = _store.Get();
        return new PicksDto(
            _copies.ThemedName,
            _copies.BestName,
            Current(state.Themed),
            Current(state.Best),
            PickCatalog.All.Select(p => new PickInfoDto(p.Id, p.Kind.ToString(), PickNames.Get(p.Id, culture))).ToList());
    }

    private static CurrentPickDto? Current(PickResult? pick) =>
        pick is null ? null : new CurrentPickDto(pick.PickId, pick.Name, pick.ItemIds.Count, pick.GeneratedUtc);
}

/// <summary>Picks overview for the admin page.</summary>
public record PicksDto(string ThemedChannel, string BestChannel, CurrentPickDto? Themed, CurrentPickDto? Best, List<PickInfoDto> Catalog);

/// <summary>What a channel shows now.</summary>
public record CurrentPickDto(string PickId, string Name, int Count, DateTime GeneratedUtc);

/// <summary>One pick of the catalog: Kind is Holiday, Franchise or Top.</summary>
public record PickInfoDto(string Id, string Kind, string Name);
