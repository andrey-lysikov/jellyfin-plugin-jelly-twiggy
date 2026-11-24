namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>Shared rules for managed collection titles and retained picks.</summary>
public static class PickCollectionRules
{
    /// <summary>Gets the current pick name, or the collection's default name when there is none.</summary>
    public static string Title(string defaultName, PickResult? result)
    {
        var names = result?.Groups
            .Select(g => g.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
        return names.Count > 0 ? string.Join(" • ", names) : defaultName;
    }

    /// <summary>Keeps the previous result when it still has allowed films that exist in the source libraries.</summary>
    public static PickResult? RetainPrevious(
        PickResult? previous,
        IReadOnlySet<Guid> existingIds,
        IReadOnlySet<string> disabled)
    {
        if (previous is null)
        {
            return null;
        }

        var groups = previous.Groups
            .Where(g => !disabled.Contains(g.PickId))
            .Select(g => new PickGroup
            {
                PickId = g.PickId,
                Name = g.Name,
                ItemIds = g.ItemIds.Where(existingIds.Contains).ToList()
            })
            .Where(g => g.ItemIds.Count > 0)
            .ToList();
        if (groups.Count == 0)
        {
            return null;
        }

        return new PickResult
        {
            Groups = groups,
            GeneratedUtc = previous.GeneratedUtc,
            Date = previous.Date
        };
    }
}
