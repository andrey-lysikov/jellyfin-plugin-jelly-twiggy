namespace Jellyfin.Plugin.JellyTwiggy.Media;

/// <summary>The one place that turns a frame size into a resolution label.</summary>
internal static class Resolution
{
    /// <summary>Gets 8K, 4K, 1080p, 720p, 576p, 480p or SD; null when the size is unknown.</summary>
    public static string? Classify(int? width, int? height)
    {
        int w = width ?? 0, h = height ?? 0;
        if (w <= 0 && h <= 0)
        {
            return null;
        }

        // Width first: scope films are much shorter than 1080/2160 rows.
        if (w >= 7000 || h >= 4000)
        {
            return "8K";
        }

        if (w >= 3200 || h >= 2000)
        {
            return "4K";
        }

        if (w >= 1800 || h >= 1000)
        {
            return "1080p";
        }

        if (w >= 1200 || h >= 700)
        {
            return "720p";
        }

        if (h >= 560)
        {
            return "576p";
        }

        return h >= 470 ? "480p" : "SD";
    }
}
