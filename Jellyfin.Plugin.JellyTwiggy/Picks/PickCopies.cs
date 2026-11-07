using System.Collections.Concurrent;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Which channel is which, and the way from a channel copy back to the library film.</summary>
public class PickCopies
{
    private readonly ILibraryManager _libraryManager;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly ConcurrentDictionary<string, Guid> _channelIds = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="PickCopies"/> class.</summary>
    public PickCopies(ILibraryManager libraryManager, IServerConfigurationManager serverConfiguration)
    {
        _libraryManager = libraryManager;
        _serverConfiguration = serverConfiguration;
    }

    /// <summary>Gets the «Coming soon!» channel name in the server language; the name is also the channel's id.</summary>
    public string ThemedName => PickNames.Get("channelThemed", _serverConfiguration.Configuration.UICulture);

    /// <summary>Gets the «Best» channel name in the server language.</summary>
    public string BestName => PickNames.Get("channelBest", _serverConfiguration.Configuration.UICulture);

    /// <summary>Gets the internal ids of both channels, «Coming soon!» first.</summary>
    public Guid[] ChannelIds => [ChannelId(ThemedName), ChannelId(BestName)];

    /// <summary>Gets the server's internal id of a channel, derived from its name as Jellyfin does.</summary>
    public Guid ChannelId(string name) =>
        _channelIds.GetOrAdd(name, n => _libraryManager.GetNewItemId("Channel " + n, typeof(Channel)));

    /// <summary>Gets the library film a copy stands for; null for anything that is not one of our copies.</summary>
    public BaseItem? GetOriginal(BaseItem item)
    {
        if (item.SourceType != SourceType.Channel
            || (item.ChannelId != ChannelId(ThemedName) && item.ChannelId != ChannelId(BestName))
            || !Guid.TryParseExact(item.ExternalId, "N", out var originalId))
        {
            return null;
        }

        return _libraryManager.GetItemById(originalId);
    }

    /// <summary>Gets the library film for a copy, or the item itself.</summary>
    public BaseItem Resolve(BaseItem item) => GetOriginal(item) ?? item;
}
