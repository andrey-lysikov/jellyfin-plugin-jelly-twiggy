using System.Collections.Concurrent;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTwiggy.Storage;

/// <summary>Per-user plugin settings. Everything is off by default.</summary>
public class UserSettings
{
    /// <summary>Gets or sets a value indicating whether intro segments are skipped automatically.</summary>
    public bool AutoSkipIntro { get; set; }

    /// <summary>Gets or sets a value indicating whether outro segments are skipped automatically.</summary>
    public bool AutoSkipOutro { get; set; }

    /// <summary>Gets or sets a value indicating whether playback pauses when the browser tab is hidden.</summary>
    public bool AutoPauseOnTabSwitch { get; set; }

    /// <summary>Gets or sets a value indicating whether playback resumes when the tab becomes visible again.</summary>
    public bool AutoResumeOnTabSwitch { get; set; }

    /// <summary>Gets or sets a value indicating whether the video goes Picture-in-Picture when the tab is hidden.</summary>
    public bool AutoPipOnTabSwitch { get; set; }

    /// <summary>Gets or sets a value indicating whether a long press on the video plays at 2x speed.</summary>
    public bool LongPressSpeedUp { get; set; }

    /// <summary>Gets or sets a value indicating whether favorite buttons, tabs and filters are hidden.</summary>
    public bool HideFavorites { get; set; }

    /// <summary>Gets or sets a value indicating whether the Google Cast button is hidden.</summary>
    public bool HideCast { get; set; }

    /// <summary>Gets or sets the corner radius of posters and cards: none, default or large.</summary>
    public string CardRadius { get; set; } = "default";

    /// <summary>Gets or sets the corner radius of buttons, fields, dialogs and menus: none, default or large.</summary>
    public string UiRadius { get; set; } = "default";

    /// <summary>Gets or sets a value indicating whether jellyfin-web's browser-only settings are kept on the server.</summary>
    public bool SyncJellyfinSettings { get; set; }

    /// <summary>Gets or sets the admin dashboard layout.</summary>
    public DashboardLayout DashboardLayout { get; set; } = new();
}

/// <summary>Admin dashboard block layout.</summary>
public class DashboardLayout
{
    /// <summary>Gets or sets the block ids in display order. Blocks missing here keep their default position after these.</summary>
    public List<string> Order { get; set; } = [];

    /// <summary>Gets or sets the ids of hidden blocks.</summary>
    public List<string> Hidden { get; set; } = [];

    /// <summary>Gets or sets block widths by id: third, half, twoThirds or full.</summary>
    public Dictionary<string, string> Sizes { get; set; } = [];
}

/// <summary>Stores <see cref="UserSettings"/> as one JSON file per user.</summary>
public class UserSettingsStore
{
    private readonly string _directory;
    private readonly ILogger<UserSettingsStore> _logger;
    private readonly ConcurrentDictionary<Guid, UserSettings> _cache = new();
    private readonly Lock _writeLock = new();

    /// <summary>Initializes a new instance of the <see cref="UserSettingsStore"/> class.</summary>
    public UserSettingsStore(IApplicationPaths applicationPaths, ILogger<UserSettingsStore> logger)
    {
        _directory = Path.Combine(JsonFiles.GetDataDirectory(applicationPaths), "users");
        Directory.CreateDirectory(_directory);
        _logger = logger;
    }

    /// <summary>Gets a user's settings, or defaults if none were saved.</summary>
    public UserSettings Get(Guid userId) => _cache.GetOrAdd(userId, Load);

    /// <summary>The only writer: changes a copy of the current settings and saves it under the lock.</summary>
    public UserSettings Update(Guid userId, Func<UserSettings, UserSettings> change)
    {
        lock (_writeLock)
        {
            var current = Get(userId);
            var updated = change(JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(current, JsonFiles.Options), JsonFiles.Options)!);
            JsonFiles.WriteAtomic(GetPath(userId), updated);
            _cache[userId] = updated;
            return updated;
        }
    }

    private UserSettings Load(Guid userId)
    {
        var path = GetPath(userId);
        if (!File.Exists(path))
        {
            return new UserSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), JsonFiles.Options) ?? new UserSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to read settings of user {UserId}, using defaults", userId);
            return new UserSettings();
        }
    }

    private string GetPath(Guid userId) => Path.Combine(_directory, userId.ToString("N") + ".json");

    /// <summary>Gets the user's synced jellyfin-web settings (localStorage key to value).</summary>
    public Dictionary<string, string> GetJellyfinSettings(Guid userId)
    {
        var path = GetJellyfinSettingsPath(userId);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonFiles.Options) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogError(ex, "Jelly Twiggy: failed to read synced Jellyfin settings of user {UserId}", userId);
            return [];
        }
    }

    /// <summary>Saves the user's synced jellyfin-web settings.</summary>
    public void SaveJellyfinSettings(Guid userId, Dictionary<string, string> values)
    {
        lock (_writeLock)
        {
            JsonFiles.WriteAtomic(GetJellyfinSettingsPath(userId), values);
        }
    }

    private string GetJellyfinSettingsPath(Guid userId) => Path.Combine(_directory, userId.ToString("N") + ".jellyfin.json");
}
