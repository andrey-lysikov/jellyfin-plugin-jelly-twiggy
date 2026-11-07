using System.Collections.Concurrent;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Which channel is which, and the way from a channel copy back to the library film.</summary>
public class PickCopies
{
    private static readonly Lock NameLock = new();

    private readonly ILibraryManager _libraryManager;
    private readonly IServerConfigurationManager _serverConfiguration;
    private readonly ConcurrentDictionary<string, Guid> _channelIds = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="PickCopies"/> class.</summary>
    public PickCopies(ILibraryManager libraryManager, IServerConfigurationManager serverConfiguration)
    {
        _libraryManager = libraryManager;
        _serverConfiguration = serverConfiguration;
    }

    /// <summary>Gets the «Themed» channel name.</summary>
    public string ThemedName => ResolveName(true);

    /// <summary>Gets the «Best» channel name.</summary>
    public string BestName => ResolveName(false);

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

    // The first name is taken from the server language and kept: renaming would create a new channel.
    private string ResolveName(bool themed)
    {
        var key = themed ? "channelThemed" : "channelBest";
        if (Plugin.Instance is not { } plugin)
        {
            return PickNames.Get(key, _serverConfiguration.Configuration.UICulture);
        }

        lock (NameLock)
        {
            var config = plugin.Configuration;
            var name = themed ? config.ThemedChannelName : config.BestChannelName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            name = PickNames.Get(key, _serverConfiguration.Configuration.UICulture);
            if (themed)
            {
                config.ThemedChannelName = name;
            }
            else
            {
                config.BestChannelName = name;
            }

            plugin.SaveConfiguration();
            return name;
        }
    }
}
