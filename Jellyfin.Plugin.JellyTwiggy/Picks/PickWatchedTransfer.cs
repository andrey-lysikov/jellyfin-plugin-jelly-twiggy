using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Carries a view of a channel copy over to the library film.</summary>
public class PickWatchedTransfer
{
    private readonly IUserDataManager _userDataManager;

    /// <summary>Initializes a new instance of the <see cref="PickWatchedTransfer"/> class.</summary>
    public PickWatchedTransfer(IUserDataManager userDataManager)
    {
        _userDataManager = userDataManager;
    }

    /// <summary>Marks the film played if it wasn't, and counts one more view with today's date either way.</summary>
    public void Apply(User user, BaseItem original)
    {
        if (_userDataManager.GetUserData(user, original) is not { } data)
        {
            return;
        }

        if (!data.Played)
        {
            data.Played = true;
            data.PlaybackPositionTicks = 0;
        }

        data.PlayCount++;
        data.LastPlayedDate = DateTime.UtcNow;
        _userDataManager.SaveUserData(user, original, data, UserDataSaveReason.PlaybackFinished, CancellationToken.None);
    }
}
