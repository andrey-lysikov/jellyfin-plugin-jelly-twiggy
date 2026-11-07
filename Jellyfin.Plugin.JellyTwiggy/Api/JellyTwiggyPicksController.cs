using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyTwiggy.Picks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
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
    private readonly IUserManager _userManager;

    /// <summary>Initializes a new instance of the <see cref="JellyTwiggyPicksController"/> class.</summary>
    public JellyTwiggyPicksController(PickStore store, PickCopies copies, IServerConfigurationManager serverConfiguration, IUserManager userManager)
    {
        _store = store;
        _copies = copies;
        _serverConfiguration = serverConfiguration;
        _userManager = userManager;
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

    /// <summary>Puts both channels first in every user's library order, so their rows lead the home screen.</summary>
    // Other views keep their order; users can still reorder them in their home screen settings.
    [HttpPost("Picks/MoveFirst")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> MoveJellyTwiggyPicksFirst()
    {
        var channels = _copies.ChannelIds;
        var users = _userManager.GetUsers().ToList();
        foreach (var user in users)
        {
            var order = user.GetPreferenceValues<Guid>(PreferenceKind.OrderedViews);
            user.SetPreference(PreferenceKind.OrderedViews, channels.Concat(order.Where(id => !channels.Contains(id))).ToArray());
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
        }

        return users.Count;
    }

    private static List<CurrentPickDto> Current(PickResult? pick) =>
        pick is null ? [] : pick.Groups.Select(g => new CurrentPickDto(g.PickId, g.Name, g.ItemIds.Count, pick.GeneratedUtc)).ToList();
}

/// <summary>Picks overview for the admin page.</summary>
public record PicksDto(string ThemedChannel, string BestChannel, List<CurrentPickDto> Themed, List<CurrentPickDto> Best, List<PickInfoDto> Catalog);

/// <summary>One folder a channel shows now.</summary>
public record CurrentPickDto(string PickId, string Name, int Count, DateTime GeneratedUtc);

/// <summary>One pick of the catalog: Kind is Holiday, Franchise or Top.</summary>
public record PickInfoDto(string Id, string Kind, string Name);
