using System.Text.Json;
using Jellyfin.Plugin.JellyTwiggy.Storage;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>One pick a collection shows: a holiday, film series or top.</summary>
public sealed class PickGroup
{
    /// <summary>Gets or sets the pick id from <see cref="PickCatalog"/>.</summary>
    public string PickId { get; set; } = string.Empty;

    /// <summary>Gets or sets the pick name in the server language.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the original library items, best first.</summary>
    public List<Guid> ItemIds { get; set; } = [];
}

/// <summary>The current content of one collection: its picks in display order.</summary>
public sealed class PickResult
{
    /// <summary>Gets or sets the picks; «Best» has one, «Coming soon!» one per nearby holiday.</summary>
    public List<PickGroup> Groups { get; set; } = [];

    /// <summary>Gets or sets when the pick was computed (UTC).</summary>
    public DateTime GeneratedUtc { get; set; }

    /// <summary>Gets or sets the server-local day it was computed for.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Gets every film of all picks in display order.</summary>
    public IEnumerable<Guid> AllItemIds() => Groups.SelectMany(g => g.ItemIds);
}

/// <summary>Saved state of both collections.</summary>
public sealed class PickState
{
    /// <summary>Gets or sets the «Themed» collection content; null when nothing has fitted yet.</summary>
    public PickResult? Themed { get; set; }

    /// <summary>Gets or sets the «Best» collection content; null when no top has fitted yet.</summary>
    public PickResult? Best { get; set; }

    /// <summary>Gets or sets how many times each person top has come round; it picks the next director or star.</summary>
    public Dictionary<string, int> Rounds { get; set; } = [];

    /// <summary>Gets or sets the managed «Themed» collection id.</summary>
    public Guid? ThemedCollectionId { get; set; }

    /// <summary>Gets or sets the managed «Best» collection id.</summary>
    public Guid? BestCollectionId { get; set; }

    /// <summary>Gets or sets whether the one-time channel cleanup has completed.</summary>
    public bool LegacyChannelsRemoved { get; set; }
}

/// <summary>Keeps <see cref="PickState"/> in memory and in picks.json.</summary>
public class PickStore
{
    private readonly string _path;
    private readonly ILogger<PickStore> _logger;
    private readonly Lock _lock = new();
    private PickState? _state;

    /// <summary>Initializes a new instance of the <see cref="PickStore"/> class.</summary>
    public PickStore(IApplicationPaths applicationPaths, ILogger<PickStore> logger)
    {
        _path = Path.Combine(JsonFiles.GetDataDirectory(applicationPaths), "picks.json");
        _logger = logger;
    }

    /// <summary>Gets the current state; callers must not change it, use <see cref="Save"/>.</summary>
    public PickState Get()
    {
        lock (_lock)
        {
            return _state ??= Load();
        }
    }

    /// <summary>Saves a new state.</summary>
    public void Save(PickState state)
    {
        lock (_lock)
        {
            JsonFiles.WriteAtomic(_path, state);
            _state = state;
        }
    }

    /// <summary>Marks the completed channel cleanup without changing the current picks.</summary>
    public void MarkLegacyChannelsRemoved()
    {
        lock (_lock)
        {
            var state = _state ??= Load();
            var updated = new PickState
            {
                Themed = state.Themed,
                Best = state.Best,
                Rounds = state.Rounds,
                ThemedCollectionId = state.ThemedCollectionId,
                BestCollectionId = state.BestCollectionId,
                LegacyChannelsRemoved = true
            };
            JsonFiles.WriteAtomic(_path, updated);
            _state = updated;
        }
    }

    private PickState Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<PickState>(File.ReadAllText(_path), JsonFiles.Options) ?? new PickState();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // The picks are recomputed by the tasks, so a broken file only costs one run.
            _logger.LogWarning(ex, "Jelly Twiggy: unreadable picks file, starting over");
        }

        return new PickState();
    }
}
