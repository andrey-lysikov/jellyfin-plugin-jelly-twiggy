using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Builds stable poster signatures and inputs for Jellyfin's 2×2 collage renderer.</summary>
public static class PickCollectionPosterRules
{
    /// <summary>Hashes the ordered collection items.</summary>
    public static string Signature(IEnumerable<Guid> itemIds)
    {
        var input = string.Join(',', itemIds.Select(id => id.ToString("N")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    /// <summary>Expands one to four poster paths to fill the collage without empty cells.</summary>
    public static IReadOnlyList<string> CollagePaths(IReadOnlyList<string> paths)
    {
        return paths.Count switch
        {
            0 => [],
            1 => [paths[0]],
            2 => [paths[0], paths[0], paths[1], paths[1]],
            3 => [paths[0], paths[1], paths[0], paths[2]],
            _ => [paths[0], paths[2], paths[1], paths[3]]
        };
    }
}
