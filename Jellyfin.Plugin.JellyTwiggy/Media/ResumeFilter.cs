using System.Security.Claims;
using Jellyfin.Plugin.JellyTwiggy.Storage;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.JellyTwiggy.Media;

/// <summary>Drops watched items from Continue Watching for users with <see cref="UserSettings.HideWatchedFromResume"/>.</summary>
/// <remarks>Jellyfin keeps a rewatched item played and resumable at once, and its resume query ignores Played.</remarks>
public class ResumeFilter : IResultFilter
{
    private static readonly HashSet<string> ResumeActions = ["GetResumeItems", "GetResumeItemsLegacy"];

    private readonly UserSettingsStore _settings;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;

    /// <summary>Initializes a new instance of the <see cref="ResumeFilter"/> class.</summary>
    public ResumeFilter(UserSettingsStore settings, IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager)
    {
        _settings = settings;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
    }

    /// <inheritdoc />
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult { Value: QueryResult<BaseItemDto> result }
            || context.ActionDescriptor is not ControllerActionDescriptor { ControllerName: "Items" } action
            || !ResumeActions.Contains(action.ActionName)
            || GetUserId(context) is not { } userId
            || !_settings.Get(userId).HideWatchedFromResume)
        {
            return;
        }

        var items = result.Items.Where(item => !IsPlayed(item, userId)).ToList();
        result.TotalRecordCount = Math.Max(0, result.TotalRecordCount - (result.Items.Count - items.Count));
        result.Items = items;
    }

    /// <inheritdoc />
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    // The list's owner: userId from the query or route (admins may ask for others), else the caller.
    private static Guid? GetUserId(ResultExecutingContext context)
    {
        var value = context.HttpContext.Request.Query["userId"].FirstOrDefault()
            ?? context.RouteData.Values["userId"]?.ToString()
            ?? context.HttpContext.User.FindFirstValue("Jellyfin-UserId");
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    // Clients that ask without user data get it looked up, with alternate versions merged as Jellyfin does.
    private bool IsPlayed(BaseItemDto dto, Guid userId)
    {
        if (dto.UserData is { } data)
        {
            return data.Played;
        }

        var user = _userManager.GetUserById(userId);
        var item = _libraryManager.GetItemById(dto.Id);
        return user is not null && item is not null && _userDataManager.GetUserDataDto(item, user)?.Played == true;
    }
}
