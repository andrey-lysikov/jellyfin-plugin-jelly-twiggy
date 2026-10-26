using Jellyfin.Plugin.JellyTwiggy.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyTwiggy;

/// <summary>Jelly Twiggy plugin entry point.</summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Initializes a new instance of the <see cref="Plugin"/> class.</summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Gets the current plugin instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Jelly Twiggy";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("71c422cd-3468-4d47-aad3-19d3d0c5be48");

    /// <inheritdoc />
    public override string Description => "Poster badges, playback options, active streams, a customizable dashboard and watch history for the Jellyfin web client.";

    /// <summary>Gets the version string used to cache-bust client files.</summary>
    public static string ClientVersion => typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = Name,
                EnableInMainMenu = true,
                MenuIcon = "trip_origin",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            }
        ];
    }
}
