using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Jellyfin.Plugin.JellyTwiggy.History;
using Jellyfin.Plugin.JellyTwiggy.Media;
using Jellyfin.Plugin.JellyTwiggy.Storage;
using Jellyfin.Plugin.JellyTwiggy.WebInjection;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace Jellyfin.Plugin.JellyTwiggy.Api;

/// <summary>Jelly Twiggy API.</summary>
[ApiController]
[Route("JellyTwiggy")]
public class JellyTwiggyController : ControllerBase
{
    private const string WebResourcePrefix = "Web/";
    private const string UserIdClaim = "Jellyfin-UserId";
    private const string AdministratorRole = "Administrator";
    private const int MaxBadgeItems = 300;

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    // Embedded client files keyed by their path under Web/, e.g. "js/jelly-twiggy.js".
    private static readonly Lazy<FrozenDictionary<string, string>> ClientFiles = new(() =>
        typeof(Plugin).Assembly.GetManifestResourceNames()
            .Select(name => (Resource: name, Path: name.Replace('\\', '/')))
            .Where(r => r.Path.StartsWith(WebResourcePrefix, StringComparison.Ordinal))
            .ToFrozenDictionary(r => r.Path[WebResourcePrefix.Length..], r => r.Resource, StringComparer.OrdinalIgnoreCase));

    private readonly UserSettingsStore _userSettings;
    private readonly MediaBadgeService _badges;
    private readonly WatchersService _watchers;
    private readonly SessionsService _sessions;
    private readonly StatisticsService _statistics;

    /// <summary>Initializes a new instance of the <see cref="JellyTwiggyController"/> class.</summary>
    public JellyTwiggyController(
        UserSettingsStore userSettings,
        MediaBadgeService badges,
        WatchersService watchers,
        SessionsService sessions,
        StatisticsService statistics)
    {
        _userSettings = userSettings;
        _badges = badges;
        _watchers = watchers;
        _sessions = sessions;
        _statistics = statistics;
    }

    private bool IsAdmin => User.IsInRole(AdministratorRole);

    /// <summary>Serves a client file. Loaded from index.html before login, so it's anonymous.</summary>
    [HttpGet("client/{**path}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetClientFile([FromRoute] string path, [FromQuery] string? v)
    {
        if (!ClientFiles.Value.TryGetValue(path, out var resource)
            || typeof(Plugin).Assembly.GetManifestResourceStream(resource) is not { } stream)
        {
            return NotFound();
        }

        if (!ContentTypes.TryGetContentType(path, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        Response.Headers.CacheControl = v == Plugin.ClientVersion
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        return File(stream, contentType);
    }

    /// <summary>Gets the plugin status for the admin page.</summary>
    [HttpGet("Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PluginStatusDto> GetStatus()
    {
        return new PluginStatusDto(Plugin.ClientVersion, FileTransformationRegistrar.IsRegistered);
    }

    /// <summary>Gets the configuration the web client needs.</summary>
    [HttpGet("ClientConfig")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ClientConfigDto> GetClientConfig()
    {
        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        return new ClientConfigDto(
            Plugin.ClientVersion,
            IsAdmin,
            config.EnablePosterBadges,
            config.EnableActiveStreams,
            config.EnableWatchedBy,
            config.EnableDashboard);
    }

    /// <summary>Gets the current user's settings.</summary>
    [HttpGet("UserSettings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<UserSettings> GetUserSettings()
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        return _userSettings.Get(userId.Value);
    }

    /// <summary>Saves the current user's playback settings. The dashboard layout is kept as is.</summary>
    [HttpPost("UserSettings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult SaveUserSettings([FromBody, Required] UserSettings settings)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        settings.DashboardLayout = _userSettings.Get(userId.Value).DashboardLayout;
        _userSettings.Save(userId.Value, settings);
        return NoContent();
    }

    /// <summary>Saves the current admin's dashboard layout.</summary>
    [HttpPost("DashboardLayout")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult SaveDashboardLayout([FromBody, Required] DashboardLayout layout)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var settings = _userSettings.Get(userId.Value);
        settings.DashboardLayout = layout;
        _userSettings.Save(userId.Value, settings);
        return NoContent();
    }

    /// <summary>Gets poster badges for several items.</summary>
    [HttpPost("Badges")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<Dictionary<Guid, ItemBadges>> GetBadges([FromBody, Required] BadgesRequest request)
    {
        var result = new Dictionary<Guid, ItemBadges>();
        foreach (var id in request.ItemIds.Distinct().Take(MaxBadgeItems))
        {
            if (_badges.GetBadges(id) is { } badges)
            {
                result[id] = badges;
            }
        }

        return result;
    }

    /// <summary>Gets the users who watched an item.</summary>
    [HttpGet("Items/{itemId}/Watchers")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<WatcherDto>> GetWatchers([FromRoute, Required] Guid itemId)
    {
        var watchers = _watchers.GetWatchers(itemId);
        if (watchers is null)
        {
            return NotFound();
        }

        return watchers;
    }

    /// <summary>Gets client sessions. Everyone sees what is playing; idle sessions and addresses are for admins.</summary>
    [HttpGet("Sessions")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<SessionDto>> GetSessions([FromQuery] bool includeIdle = false)
    {
        var isAdmin = IsAdmin;
        return _sessions.GetSessions(!(includeIdle && isAdmin), TimeSpan.FromHours(1), isAdmin);
    }

    /// <summary>Gets playback statistics.</summary>
    [HttpGet("Statistics")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<StatisticsDto> GetStatistics([FromQuery, Range(1, 3650)] int days = 7)
    {
        return _statistics.GetStatistics(days);
    }

    private Guid? GetUserId()
    {
        var value = User.FindFirstValue(UserIdClaim);
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }
}

/// <summary>Plugin status.</summary>
public record PluginStatusDto(string Version, bool FileTransformationRegistered);

/// <summary>Configuration for the web client.</summary>
public record ClientConfigDto(
    string Version,
    bool IsAdmin,
    bool EnablePosterBadges,
    bool EnableActiveStreams,
    bool EnableWatchedBy,
    bool EnableDashboard);

/// <summary>Badges request.</summary>
public class BadgesRequest
{
    /// <summary>Gets or sets the item ids.</summary>
    public Guid[] ItemIds { get; set; } = [];
}
