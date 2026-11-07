using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyTwiggy.Configuration;

/// <summary>Server-wide plugin configuration, edited by admins on the plugin page.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets a value indicating whether quality, audio and rating badges are shown on posters.</summary>
    public bool EnablePosterBadges { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the active streams header button is shown.</summary>
    public bool EnableActiveStreams { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the active sessions button ring spins while something plays.</summary>
    public bool AnimateActiveStreams { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the "Watched by" row is shown on item pages.</summary>
    public bool EnableWatchedBy { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the admin dashboard customizations are enabled.</summary>
    public bool EnableDashboard { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether item pages are restyled (track selects, age rating).</summary>
    public bool TweakItemPage { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the player volume is shown only in the bottom bar.</summary>
    public bool TweakPlayer { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether cards get badge depth, stream backdrops and the watched circle.</summary>
    public bool StyleCards { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the «Themed» and «Best» channels are shown.</summary>
    public bool EnablePicks { get; set; } = true;

    /// <summary>Gets or sets the libraries whose films may appear in the channels; none means the channels stay empty.</summary>
    public Guid[] PickSourceLibraries { get; set; } = [];

    /// <summary>Gets or sets the «Themed» channel name; set from the server language on first use. It is the channel's id.</summary>
    public string ThemedChannelName { get; set; } = string.Empty;

    /// <summary>Gets or sets the «Best» channel name; set from the server language on first use. It is the channel's id.</summary>
    public string BestChannelName { get; set; } = string.Empty;

    /// <summary>Gets or sets the ids of themes and tops that are never picked.</summary>
    public string[] DisabledPicks { get; set; } = [];
}
