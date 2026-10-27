using Jellyfin.Plugin.JellyTwiggy.Dashboard;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyTwiggy.Api;

/// <summary>Admin dashboard data: library resolutions and system load.</summary>
[ApiController]
[Route("JellyTwiggy")]
[Authorize(Policy = Policies.RequiresElevation)]
public class JellyTwiggyDashboardController : ControllerBase
{
    private readonly LibraryCountsService _libraryCounts;
    private readonly SystemMetricsService _systemMetrics;

    /// <summary>Initializes a new instance of the <see cref="JellyTwiggyDashboardController"/> class.</summary>
    public JellyTwiggyDashboardController(LibraryCountsService libraryCounts, SystemMetricsService systemMetrics)
    {
        _libraryCounts = libraryCounts;
        _systemMetrics = systemMetrics;
    }

    /// <summary>Gets item counts per resolution, keyed by item kind.</summary>
    [HttpGet("LibraryResolutions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<Dictionary<string, Dictionary<string, int>>> GetJellyTwiggyLibraryResolutions()
    {
        return _libraryCounts.GetCounts();
    }

    /// <summary>Gets current CPU, memory and disk load.</summary>
    [HttpGet("SystemMetrics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemMetricsDto>> GetJellyTwiggySystemMetrics(CancellationToken cancellationToken)
    {
        return await _systemMetrics.GetMetricsAsync(cancellationToken).ConfigureAwait(false);
    }
}
