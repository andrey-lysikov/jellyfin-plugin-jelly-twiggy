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
    private List<PlaybackRecord>? _records;

    /// <summary>Initializes a new instance of the <see cref="PlaybackHistoryStore"/> class.</summary>
    public PlaybackHistoryStore(IApplicationPaths applicationPaths, ILogger<PlaybackHistoryStore> logger)
    {
        _path = Path.Combine(JsonFiles.GetDataDirectory(applicationPaths), "playback-history.jsonl");
        _logger = logger;
    }

    /// <summary>Appends a record.</summary>
    public void Add(PlaybackRecord record)
    {
        var line = JsonSerializer.Serialize(record, JsonFiles.Options) + "\n";
        lock (_lock)
        {
            EnsureLoaded().Add(record);
            File.AppendAllText(_path, line);
        }
    }

    /// <summary>Returns a snapshot of the records matching a filter.</summary>
    public List<PlaybackRecord> Find(Func<PlaybackRecord, bool> predicate)
    {
        lock (_lock)
        {
            return EnsureLoaded().Where(predicate).ToList();
        }
    }

    private List<PlaybackRecord> EnsureLoaded()
    {
        if (_records is not null)
        {
            return _records;
        }

        _records = [];
        if (!File.Exists(_path))
        {
            return _records;
        }

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
                    _records.Add(record);
                }
            }
            catch (JsonException ex)
            {
                // A torn last line after a crash must not lose the rest of the history.
                _logger.LogWarning(ex, "Jelly Twiggy: skipping unreadable playback history line {Line}", lineNumber);
            }
        }

        // Terminate a torn last line so the next append doesn't get glued to it.
        using (var file = File.Open(_path, FileMode.Open, FileAccess.ReadWrite))
        {
            if (file.Length > 0)
            {
                file.Seek(-1, SeekOrigin.End);
                if (file.ReadByte() != '\n')
                {
                    file.WriteByte((byte)'\n');
                }
            }
        }

        return _records;
    }
}
