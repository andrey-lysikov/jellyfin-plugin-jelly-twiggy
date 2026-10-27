using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyTwiggy.Configuration;

/// <summary>Server-wide plugin configuration, edited by admins on the plugin page.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets a value indicating whether quality, audio and rating badges are shown on posters.</summary>
    public bool EnablePosterBadges { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the active streams header button is shown.</summary>
    public bool EnableActiveStreams { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the "Watched by" row is shown on item pages.</summary>
    public bool EnableWatchedBy { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the admin dashboard customizations are enabled.</summary>
    public bool EnableDashboard { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether item pages are restyled (track selects, age rating, video badges).</summary>
    public bool TweakItemPage { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the player volume is shown only in the bottom bar.</summary>
    public bool TweakPlayer { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether animations (streams ring, watched pop) are on.</summary>
    public bool EnableAnimations { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether cards get badge depth, stream backdrops and the watched circle.</summary>
    public bool StyleCards { get; set; } = true;
}
