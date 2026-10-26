using System.Text.Json;
using System.Text.Json.Serialization;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.JellyTwiggy.Storage;

/// <summary>Shared settings for the plugin's on-disk JSON files.</summary>
public static class JsonFiles
{
    /// <summary>Serializer options for all plugin files.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Gets the data directory; outside the plugin folder, so it survives updates.</summary>
    public static string GetDataDirectory(IApplicationPaths applicationPaths)
    {
        var path = Path.Combine(applicationPaths.DataPath, "jellytwiggy");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes a file atomically: a crash mid-write leaves the previous version intact.</summary>
    public static void WriteAtomic<T>(string path, T value)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
