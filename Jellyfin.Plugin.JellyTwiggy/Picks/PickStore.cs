using System.Text.Json;
using Jellyfin.Plugin.JellyTwiggy.Storage;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.Picks;

/// <summary>One folder of a channel: a holiday, film series or top.</summary>
public sealed class PickGroup
{
    /// <summary>Gets or sets the pick id from <see cref="PickCatalog"/>.</summary>
    public string PickId { get; set; } = string.Empty;

    /// <summary>Gets or sets the pick name in the server language; it names the folder.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the original library items, best first.</summary>
    public List<Guid> ItemIds { get; set; } = [];

    /// <summary>Gets or sets the film whose poster the folder shows, picked at random.</summary>
    public Guid? PosterId { get; set; }
}

/// <summary>The current content of one channel: its folders in display order.</summary>
public sealed class PickResult
{
    /// <summary>Gets or sets the folders; the «Best» channel has one, «Coming soon!» one per nearby holiday.</summary>
    public List<PickGroup> Groups { get; set; } = [];

    /// <summary>Gets or sets when the pick was computed (UTC); copies get creation dates just before it.</summary>
    public DateTime GeneratedUtc { get; set; }

    /// <summary>Gets or sets the server-local day it was computed for.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Gets every film of all folders in display order; the rank in it sets the copy's creation date.</summary>
    public IEnumerable<Guid> AllItemIds() => Groups.SelectMany(g => g.ItemIds);

    /// <summary>Gets the channel folder id of a group: it changes with every new pick, so the folder gets a fresh poster.</summary>
    public string FolderId(PickGroup group) =>
        $"{group.PickId}-{GeneratedUtc.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>Saved state of both channels.</summary>
public sealed class PickState
{
    /// <summary>Gets or sets the «Themed» channel content; null when nothing fits.</summary>
    public PickResult? Themed { get; set; }

    /// <summary>Gets or sets the «Best» channel content; null when no top has enough films.</summary>
    public PickResult? Best { get; set; }

    /// <summary>Gets or sets the index of the current top in <see cref="PickCatalog.Tops"/>; -1 before the first.</summary>
    public int BestIndex { get; set; } = -1;
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
