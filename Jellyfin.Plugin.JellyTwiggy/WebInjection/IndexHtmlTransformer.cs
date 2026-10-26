namespace Jellyfin.Plugin.JellyTwiggy.WebInjection;

/// <summary>Payload passed by File Transformation to the callback; it deserializes <c>{ "contents": "..." }</c> into this type.</summary>
public class TransformationPayload
{
    /// <summary>Gets or sets the current file contents.</summary>
    public string? Contents { get; set; }
}

/// <summary>Adds the Jelly Twiggy client script to jellyfin-web's index.html.</summary>
public static class IndexHtmlTransformer
{
    private const string ScriptMarker = "plugin=\"JellyTwiggy\"";

    /// <summary>File Transformation callback: returns index.html with the client script tag added before &lt;/body&gt;.</summary>
    public static string Transform(TransformationPayload payload)
    {
        var html = payload.Contents ?? string.Empty;
        if (html.Contains(ScriptMarker, StringComparison.Ordinal))
        {
            return html;
        }

        var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyClose < 0)
        {
            return html;
        }

        // Relative to /web/index.html, so it also works when Jellyfin runs under a base URL.
        var tag = $"<script {ScriptMarker} src=\"../JellyTwiggy/client/js/jelly-twiggy.js?v={Plugin.ClientVersion}\" defer></script>\n";
        return html.Insert(bodyClose, tag);
    }
}
