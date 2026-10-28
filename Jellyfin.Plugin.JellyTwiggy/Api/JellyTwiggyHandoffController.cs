using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Jellyfin.Plugin.JellyTwiggy.Handoff;
using Jellyfin.Plugin.JellyTwiggy.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyTwiggy.Api;

/// <summary>"Continue here" and "Play on" between the current user's devices and Swiftly Player.</summary>
[ApiController]
[Route("JellyTwiggy/Handoff")]
[Authorize]
public class JellyTwiggyHandoffController : ControllerBase
{
    private const string UserIdClaim = "Jellyfin-UserId";
    private const string DeviceIdClaim = "Jellyfin-DeviceId";
    private const string AdministratorRole = "Administrator";

    private readonly HandoffService _handoff;
    private readonly UserSettingsStore _userSettings;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;

    /// <summary>Initializes a new instance of the <see cref="JellyTwiggyHandoffController"/> class.</summary>
    public JellyTwiggyHandoffController(
        HandoffService handoff,
        UserSettingsStore userSettings,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        IUserManager userManager)
    {
        _handoff = handoff;
        _userSettings = userSettings;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
    }

    /// <summary>Gets the caller's sessions on other devices that can be taken over, freshest first.</summary>
    [HttpGet("Sources")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<HandoffSessionDto>> GetJellyTwiggyHandoffSources()
    {
        if (GetCaller() is not { } caller)
        {
            return Unauthorized();
        }

        return Enabled(caller.UserId) ? _handoff.Sources(caller.UserId, caller.DeviceId).Select(ToDto).ToList() : [];
    }

    /// <summary>Gets the caller's devices that can receive "Play on", one per device.</summary>
    [HttpGet("Targets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<HandoffSessionDto>> GetJellyTwiggyHandoffTargets()
    {
        if (GetCaller() is not { } caller)
        {
            return Unauthorized();
        }

        return Enabled(caller.UserId) ? _handoff.Targets(caller.UserId, caller.DeviceId).Select(ToDto).ToList() : [];
    }

    /// <summary>Debugging: why each session is or isn't offered. Admins see every user's sessions.</summary>
    [HttpGet("Explain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<HandoffExplainDto>> GetJellyTwiggyHandoffExplain()
    {
        if (GetCaller() is not { } caller)
        {
            return Unauthorized();
        }

        return _handoff.Explain(caller.UserId, caller.DeviceId, User.IsInRole(AdministratorRole))
            .Select(e => new HandoffExplainDto(e.Session.Id, e.Session.UserId, e.Session.DeviceName, e.Session.Client, e.Takeover, e.Target))
            .ToList();
    }

    /// <summary>Stops one of the caller's sessions on another device and continues its video here.</summary>
    [HttpPost("Takeover/{sessionId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HandoffResultDto>> JellyTwiggyHandoffTakeover([FromRoute, Required] string sessionId, CancellationToken cancellationToken)
    {
        if (GetCaller() is not { } caller)
        {
            return Unauthorized();
        }

        if (!Enabled(caller.UserId))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await _handoff.TakeOver(caller.UserId, caller.DeviceId, sessionId, cancellationToken).ConfigureAwait(false);
        return ToResponse(result);
    }

    /// <summary>Sends an item, or what this device's player plays, to another device of the caller.</summary>
    [HttpPost("Play")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HandoffResultDto>> JellyTwiggyHandoffPlay([FromBody, Required] HandoffPlayRequest request, CancellationToken cancellationToken)
    {
        if (GetCaller() is not { } caller)
        {
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(request.DeviceId) || request.StartPositionTicks < 0)
        {
            return BadRequest();
        }

        if (!Enabled(caller.UserId))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request.FromPlayer)
        {
            var moved = await _handoff.MoveFromPlayer(caller.UserId, caller.DeviceId, request.DeviceId, request.StartPositionTicks, cancellationToken)
                .ConfigureAwait(false);
            return ToResponse(moved);
        }

        // Only items the caller can see; without a start position a started item resumes where it was left.
        var user = _userManager.GetUserById(caller.UserId);
        var item = user is null || request.ItemId is not { } itemId ? null : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        var start = request.StartPositionTicks ?? (item.IsFolder ? null : _userDataManager.GetUserData(user!, item)?.PlaybackPositionTicks);
        var play = new HandoffPlay(item.Id, start, request.MediaSourceId, request.AudioStreamIndex, request.SubtitleStreamIndex);
        var result = await _handoff.PlayOn(caller.UserId, caller.DeviceId, request.DeviceId, play, cancellationToken).ConfigureAwait(false);
        return ToResponse(result);
    }

    private static HandoffSessionDto ToDto(HandoffSession s) =>
        new(s.Id, s.DeviceId, s.DeviceName, s.Client, s.ItemId, s.PositionTicks, s.LastActivityDate);

    private ActionResult<HandoffResultDto> ToResponse(HandoffResult result)
    {
        var dto = new HandoffResultDto(result.Status.ToString(), result.DeviceName);
        return result.Status switch
        {
            HandoffStatus.Ok => dto,
            HandoffStatus.NoOwnSession => Conflict(dto),
            _ => NotFound(dto)
        };
    }

    // Off by default: the user turns it on in their Jelly Twiggy settings.
    private bool Enabled(Guid userId) => _userSettings.Get(userId).EnableHandoff;

    private (Guid UserId, string DeviceId)? GetCaller()
    {
        var deviceId = User.FindFirstValue(DeviceIdClaim);
        return Guid.TryParse(User.FindFirstValue(UserIdClaim), out var id) && id != Guid.Empty && !string.IsNullOrEmpty(deviceId)
            ? (id, deviceId)
            : null;
    }
}

/// <summary>A session offered for "Continue here" or "Play on".</summary>
public record HandoffSessionDto(string SessionId, string? DeviceId, string? DeviceName, string? Client, Guid? ItemId, long? PositionTicks, DateTime LastActivityDate);

/// <summary>Why a session is or isn't offered; null reasons mean it is.</summary>
public record HandoffExplainDto(string SessionId, Guid UserId, string? DeviceName, string? Client, string? Takeover, string? Target);

/// <summary>Outcome of a handoff: Ok, NoOwnSession, SourceGone or TargetGone, and the other device's name.</summary>
public record HandoffResultDto(string Status, string? DeviceName);

/// <summary>"Play on" request. With <see cref="FromPlayer"/> the item and tracks come from this device's player.</summary>
public class HandoffPlayRequest
{
    /// <summary>Gets or sets the target device id.</summary>
    public string? DeviceId { get; set; }

    /// <summary>Gets or sets the item to play.</summary>
    public Guid? ItemId { get; set; }

    /// <summary>Gets or sets the start position; null resumes a started item.</summary>
    public long? StartPositionTicks { get; set; }

    /// <summary>Gets or sets the chosen version.</summary>
    public string? MediaSourceId { get; set; }

    /// <summary>Gets or sets the chosen audio track.</summary>
    public int? AudioStreamIndex { get; set; }

    /// <summary>Gets or sets the chosen subtitles; -1 turns them off.</summary>
    public int? SubtitleStreamIndex { get; set; }

    /// <summary>Gets or sets a value indicating whether to move this device's current playback and stop it here.</summary>
    public bool FromPlayer { get; set; }
}
