using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

// DEPRECATED: one-release migration from plugin channels to collections. Remove this class in the next release.
/// <summary>Deletes the channels and channel copies created by older plugin versions.</summary>
public class LegacyChannelCleanup
{
    private static readonly string[] ThemedNames =
    [
        "Coming soon!", "Уже скоро!", "Demnächst!", "قريبًا!", "Ужо хутка!", "Už brzy!", "Bientôt !",
        "Presto!", "もうすぐ！", "Jaqynda!", "Już wkrótce!", "Ускоро!", "即将到来！"
    ];

    private static readonly string[] BestNames =
    [
        "Best", "Лучшие", "Das Beste", "الأفضل", "Лепшыя", "Nejlepší", "Le meilleur", "Il meglio",
        "ベスト", "Üzdıkter", "Najlepsze", "Најбоље", "最佳"
    ];

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<LegacyChannelCleanup> _logger;

    /// <summary>Initializes a new instance of the <see cref="LegacyChannelCleanup"/> class.</summary>
    public LegacyChannelCleanup(
        ILibraryManager libraryManager,
        IUserManager userManager,
        ILogger<LegacyChannelCleanup> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>Removes every known legacy channel id from users and the library.</summary>
    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        var ids = ThemedNames.Concat(BestNames)
            .Select(name => _libraryManager.GetNewItemId("Channel " + name, typeof(Channel)))
            .ToHashSet();
        foreach (var user in _userManager.GetUsers())
        {
            var order = user.GetPreferenceValues<Guid>(PreferenceKind.OrderedViews);
            var updated = order.Where(id => !ids.Contains(id)).ToArray();
            if (updated.Length != order.Length)
            {
                user.SetPreference(PreferenceKind.OrderedViews, updated);
                await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            }
        }

        var removed = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_libraryManager.GetItemById(id) is Channel channel)
            {
                _libraryManager.DeleteItem(channel, new DeleteOptions { DeleteFileLocation = false });
                removed++;
            }
        }

        _logger.LogInformation("Jelly Twiggy: removed {Count} legacy pick channels", removed);
    }
}
