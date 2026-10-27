using System.Text.Json;
using Jellyfin.Plugin.JellyTwiggy.Storage;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.History;

/// <summary>Append-only playback log kept forever, as JSON lines held in memory (records are small).</summary>
public class PlaybackHistoryStore
{
    private readonly string _path;
    private readonly ILogger<PlaybackHistoryStore> _logger;
    private readonly Lock _lock = new();
    private Index? _index;

    /// <summary>Initializes a new instance of the <see cref="PlaybackHistoryStore"/> class.</summary>
    public PlaybackHistoryStore(IApplicationPaths applicationPaths, ILogger<PlaybackHistoryStore> logger)
    {
        _path = Path.Combine(JsonFiles.GetDataDirectory(applicationPaths), "playback-history.jsonl");
        _logger = logger;
    }

    /// <summary>Appends a record: disk first, so memory never holds what the file lost.</summary>
    public void Add(PlaybackRecord record)
    {
        var line = JsonSerializer.Serialize(record, JsonFiles.Options) + "\n";
        lock (_lock)
        {
            var index = EnsureLoaded();
            File.AppendAllText(_path, line);
            index.Add(record);
        }
    }

    /// <summary>Returns a snapshot of all records matching a filter, in chronological order.</summary>
    public List<PlaybackRecord> Find(Func<PlaybackRecord, bool> predicate)
    {
        lock (_lock)
        {
            return EnsureLoaded().All.Where(predicate).ToList();
        }
    }

    /// <summary>Returns a snapshot of one user's records.</summary>
    public List<PlaybackRecord> ForUser(Guid userId)
    {
        lock (_lock)
        {
            return EnsureLoaded().ByUser.TryGetValue(userId, out var records) ? [.. records] : [];
        }
    }

    /// <summary>Returns a snapshot of one user's records of one item.</summary>
    public List<PlaybackRecord> ForUserItem(Guid userId, Guid itemId)
    {
        lock (_lock)
        {
            return EnsureLoaded().ByUserItem.TryGetValue((userId, itemId), out var records) ? [.. records] : [];
        }
    }

    // Built into a local index and published only after the whole file was read.
    private Index EnsureLoaded()
    {
        if (_index is not null)
        {
            return _index;
        }

        var index = new Index();
        if (File.Exists(_path))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(_path))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize<PlaybackRecord>(line, JsonFiles.Options) is { } record)
                    {
                        index.Add(record);
                    }
                }
                catch (JsonException ex)
                {
                    // A torn last line after a crash must not lose the rest of the history.
                    _logger.LogWarning(ex, "Jelly Twiggy: skipping unreadable playback history line {Line}", lineNumber);
                }
            }

            TerminateLastLine();
        }

        _index = index;
        return index;
    }

    // Terminate a torn last line so the next append doesn't get glued to it.
    private void TerminateLastLine()
    {
        using var file = File.Open(_path, FileMode.Open, FileAccess.ReadWrite);
        if (file.Length > 0)
        {
            file.Seek(-1, SeekOrigin.End);
            if (file.ReadByte() != '\n')
            {
                file.WriteByte((byte)'\n');
            }
        }
    }

    private sealed class Index
    {
        public List<PlaybackRecord> All { get; } = [];

        public Dictionary<Guid, List<PlaybackRecord>> ByUser { get; } = [];

        public Dictionary<(Guid UserId, Guid ItemId), List<PlaybackRecord>> ByUserItem { get; } = [];

        public void Add(PlaybackRecord record)
        {
            All.Add(record);
            Append(ByUser, record.UserId, record);
            Append(ByUserItem, (record.UserId, record.ItemId), record);
        }

        private static void Append<TKey>(Dictionary<TKey, List<PlaybackRecord>> map, TKey key, PlaybackRecord record)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out var list))
            {
                map[key] = list = [];
            }

            list.Add(record);
        }
    }
}
