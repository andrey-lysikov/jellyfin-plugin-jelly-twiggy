using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyTwiggy.History;
using Jellyfin.Plugin.JellyTwiggy.Media;
using Jellyfin.Plugin.JellyTwiggy.Storage;
using Jellyfin.Plugin.JellyTwiggy.WebInjection;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
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
    private const int MaxSyncedSettings = 500;
    private const int MaxSyncedChars = 1_000_000;
    private const int MaxLayoutBlocks = 50;
    private const int MaxBlockIdLength = 64;

    private static readonly FrozenSet<string> RadiusChoices = new[] { "none", "default", "large" }.ToFrozenSet(StringComparer.Ordinal);

    // Block widths in grid columns, plus the names older layouts used.
    private static readonly FrozenSet<string> SizeChoices =
        new[] { "1", "2", "3", "4", "5", "6", "third", "half", "twoThirds", "full" }.ToFrozenSet(StringComparer.Ordinal);

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
    private readonly IUserManager _userManager;

    /// <summary>Initializes a new instance of the <see cref="JellyTwiggyController"/> class.</summary>
    public JellyTwiggyController(
        UserSettingsStore userSettings,
        MediaBadgeService badges,
        WatchersService watchers,
        SessionsService sessions,
        StatisticsService statistics,
        IUserManager userManager)
    {
        _userSettings = userSettings;
        _badges = badges;
        _watchers = watchers;
        _sessions = sessions;
        _statistics = statistics;
        _userManager = userManager;
    }

    private bool IsAdmin => User.IsInRole(AdministratorRole);

    /// <summary>Serves a client file. Loaded from index.html before login, so it's anonymous.</summary>
    [HttpGet("client/{**path}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetJellyTwiggyClientFile([FromRoute] string path, [FromQuery] string? v)
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
    public ActionResult<PluginStatusDto> GetJellyTwiggyStatus()
    {
        return new PluginStatusDto(Plugin.ClientVersion, FileTransformationRegistrar.IsRegistered);
    }

    /// <summary>Gets the configuration the web client needs.</summary>
    [HttpGet("ClientConfig")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ClientConfigDto> GetJellyTwiggyClientConfig()
    {
        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        return new ClientConfigDto(
            Plugin.ClientVersion,
            IsAdmin,
            config.EnablePosterBadges,
            config.EnableActiveStreams,
            config.EnableWatchedBy,
            config.EnableDashboard,
            config.TweakItemPage,
            config.TweakPlayer,
            config.EnableAnimations,
            config.StyleCards);
    }

    /// <summary>Gets the current user's settings.</summary>
    [HttpGet("UserSettings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<UserSettings> GetJellyTwiggyUserSettings()
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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult SaveJellyTwiggyUserSettings([FromBody, Required] UserSettings settings)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (settings.CardRadius is not { } card || settings.UiRadius is not { } ui
            || !RadiusChoices.Contains(card) || !RadiusChoices.Contains(ui))
        {
            return BadRequest();
        }

        _userSettings.Update(userId.Value, current =>
        {
            settings.DashboardLayout = current.DashboardLayout;
            return settings;
        });
        return NoContent();
    }

    /// <summary>Gets the current user's synced jellyfin-web settings.</summary>
    [HttpGet("JellyfinSettings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Dictionary<string, string>> GetJellyTwiggyJellyfinSettings()
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        return _userSettings.GetJellyfinSettings(userId.Value);
    }

    /// <summary>Replaces the current user's synced jellyfin-web settings.</summary>
    [HttpPost("JellyfinSettings")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult SaveJellyTwiggyJellyfinSettings([FromBody, Required] Dictionary<string, string> values)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        // Generous for custom CSS, but bounded: this is a settings file, not storage.
        // Only jellyfin-web's own per-user and per-device keys are synced.
        if (values.Count > MaxSyncedSettings
            || values.Sum(v => v.Key.Length + (v.Value?.Length ?? 0)) > MaxSyncedChars
            || values.Keys.Any(k => !k.StartsWith("user:", StringComparison.Ordinal) && !k.StartsWith("device:", StringComparison.Ordinal)))
        {
            return BadRequest();
        }

        _userSettings.SaveJellyfinSettings(userId.Value, values);
        return NoContent();
    }

    /// <summary>Saves the current admin's dashboard layout.</summary>
    [HttpPost("DashboardLayout")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult SaveJellyTwiggyDashboardLayout([FromBody, Required] DashboardLayout layout)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!IsValidLayout(layout))
        {
            return BadRequest();
        }

        _userSettings.Update(userId.Value, current =>
        {
            current.DashboardLayout = layout;
            return current;
        });
        return NoContent();
    }

    /// <summary>Gets poster badges for several items.</summary>
    [HttpPost("Badges")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Dictionary<Guid, ItemBadges>> GetJellyTwiggyBadges([FromBody, Required] BadgesRequest request)
    {
        if (request.ItemIds is null)
        {
            return BadRequest();
        }

        if (GetCurrentUser() is not { } viewer)
        {
            return Unauthorized();
        }

        // Cap before Distinct, so a huge body isn't hashed in full.
        var result = new Dictionary<Guid, ItemBadges>();
        foreach (var id in request.ItemIds.Take(MaxBadgeItems).Distinct())
        {
            if (_badges.GetBadges(id, viewer) is { } badges)
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<WatcherDto>> GetJellyTwiggyWatchers([FromRoute, Required] Guid itemId)
    {
        if (GetCurrentUser() is not { } viewer)
        {
            return Unauthorized();
        }

        // Null for a missing item and for one the viewer can't see alike.
        var watchers = _watchers.GetWatchers(itemId, viewer);
        if (watchers is null)
        {
            return NotFound();
        }

        return watchers;
    }

    /// <summary>Gets client sessions: admins see all, other users only their own. Idle sessions are for admins.</summary>
    [HttpGet("Sessions")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<SessionDto>> GetJellyTwiggySessions([FromQuery] bool includeIdle = false)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var isAdmin = IsAdmin;
        return _sessions.GetSessions(userId.Value, isAdmin, !(includeIdle && isAdmin), TimeSpan.FromHours(1));
    }

    /// <summary>Gets playback statistics.</summary>
    [HttpGet("Statistics")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<StatisticsDto> GetJellyTwiggyStatistics([FromQuery, Range(0, 36500)] int days = 7)
    {
        return _statistics.GetStatistics(days);
    }

    private static bool IsValidLayout(DashboardLayout layout)
    {
        static bool IsId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= MaxBlockIdLength;

        return layout.Order is { Count: <= MaxLayoutBlocks } order && order.All(IsId)
            && layout.Hidden is { Count: <= MaxLayoutBlocks } hidden && hidden.All(IsId)
            && layout.Sizes is { Count: <= MaxLayoutBlocks } sizes
            && sizes.All(s => IsId(s.Key) && s.Value is not null && SizeChoices.Contains(s.Value));
    }

    private Guid? GetUserId()
    {
        var value = User.FindFirstValue(UserIdClaim);
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    private User? GetCurrentUser()
    {
        return GetUserId() is { } id ? _userManager.GetUserById(id) : null;
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
    bool EnableDashboard,
    bool TweakItemPage,
    bool TweakPlayer,
    bool EnableAnimations,
    bool StyleCards);

/// <summary>Badges request.</summary>
public class BadgesRequest
{
    /// <summary>Gets or sets the item ids.</summary>
    public Guid[] ItemIds { get; set; } = [];
}
