using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyTwiggy.Configuration;

/// <summary>Server-wide plugin configuration, edited by admins on the plugin page.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the playback percentage at which a play counts as a completed view.</summary>
    public int CompletedThresholdPercent { get; set; } = 90;

    /// <summary>Gets or sets a value indicating whether quality, audio and rating badges are shown on posters.</summary>
    public bool EnablePosterBadges { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the active streams header button is shown.</summary>
    public bool EnableActiveStreams { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the "Watched by" row is shown on item pages.</summary>
    public bool EnableWatchedBy { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the admin dashboard customizations are enabled.</summary>
    public bool EnableDashboard { get; set; } = true;
}
