using System.Collections.Concurrent;
using System.Globalization;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyTwiggy.Media;

/// <summary>Poster badges of one item.</summary>
public record ItemBadges(string? Quality, string? Audio, float? Rating);

/// <summary>Computes poster badges from media streams.</summary>
public class MediaBadgeService
{
    // Series and seasons take their badges from an episode, so new episodes don't change the
    // item's own modification date; re-check them periodically instead.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ConcurrentDictionary<Guid, CacheEntry> _cache = new();

    /// <summary>Initializes a new instance of the <see cref="MediaBadgeService"/> class.</summary>
    public MediaBadgeService(ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
    }

    /// <summary>Gets badges for an item.</summary>
    public ItemBadges? GetBadges(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(itemId, out var cached)
            && cached.ItemModified == item.DateModified
            && now - cached.CachedAt < CacheLifetime)
        {
            return cached.Badges;
        }

        var badges = Compute(item);
        _cache[itemId] = new CacheEntry(item.DateModified, now, badges);
        return badges;
    }

    private ItemBadges Compute(BaseItem item)
    {
        var mediaItem = item.GetBaseItemKind() is BaseItemKind.Series or BaseItemKind.Season
            ? FindEpisode(item)
            : item;

        string? quality = null;
        string? audio = null;
        if (mediaItem is not null && !mediaItem.IsFolder)
        {
            var streams = _mediaSourceManager.GetMediaStreams(mediaItem.Id);
            var video = streams.Where(s => s.Type == MediaStreamType.Video).OrderBy(s => s.Index).FirstOrDefault();
            var firstAudio = streams.Where(s => s.Type == MediaStreamType.Audio).OrderBy(s => s.Index).FirstOrDefault();
            quality = video is null ? null : FormatQuality(video);
            audio = firstAudio is null ? null : FormatAudio(firstAudio);
        }

        var rating = item.CommunityRating is > 0 ? (float?)MathF.Round(item.CommunityRating.Value, 1) : null;
        return new ItemBadges(quality, audio, rating);
    }

    private BaseItem? FindEpisode(BaseItem parent)
    {
        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            AncestorIds = [parent.Id],
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true,
            IsVirtualItem = false,
            Limit = 1
        }).FirstOrDefault();
    }

    /// <summary>Formats video quality like "4K HDR (HEVC)" or "1080p (x264)".</summary>
    internal static string FormatQuality(MediaStream stream)
    {
        var label = FormatResolution(stream.Width ?? 0, stream.Height ?? 0);
        var range = FormatRange(stream.VideoRangeType);
        if (range is not null)
        {
            label += " " + range;
        }

        var codec = FormatVideoCodec(stream.Codec);
        return codec is null ? label : $"{label} ({codec})";
    }

    /// <summary>Formats an audio track like "DTS 5.1".</summary>
    internal static string FormatAudio(MediaStream stream)
    {
        var codec = FormatAudioCodec(stream);
        var channels = stream.Channels switch
        {
            null or <= 0 => null,
            1 => "1.0",
            2 => "2.0",
            3 => "2.1",
            6 => "5.1",
            7 => "6.1",
            8 => "7.1",
            var n => n.Value.ToString(CultureInfo.InvariantCulture) + "ch"
        };

        return channels is null ? codec : $"{codec} {channels}";
    }

    private static string FormatResolution(int width, int height)
    {
        // Width first: scope films are much shorter than 1080/2160 rows.
        if (width >= 7000 || height >= 4000)
        {
            return "8K";
        }

        if (width >= 3200 || height >= 2000)
        {
            return "4K";
        }

        if (width >= 1800 || height >= 1000)
        {
            return "1080p";
        }

        if (width >= 1200 || height >= 700)
        {
            return "720p";
        }

        if (height >= 560)
        {
            return "576p";
        }

        return height >= 470 ? "480p" : "SD";
    }

    private static string? FormatRange(VideoRangeType rangeType) => rangeType switch
    {
        VideoRangeType.DOVI
            or VideoRangeType.DOVIWithHDR10
            or VideoRangeType.DOVIWithHLG
            or VideoRangeType.DOVIWithSDR
            or VideoRangeType.DOVIWithEL
            or VideoRangeType.DOVIWithHDR10Plus
            or VideoRangeType.DOVIWithELHDR10Plus => "DV",
        VideoRangeType.HDR10Plus => "HDR10+",
        VideoRangeType.HDR10 or VideoRangeType.DOVIInvalid => "HDR",
        VideoRangeType.HLG => "HLG",
        _ => null
    };

    private static string? FormatVideoCodec(string? codec) => codec?.ToLowerInvariant() switch
    {
        null or "" => null,
        "h264" or "avc" => "x264",
        "hevc" or "h265" => "HEVC",
        "av1" => "AV1",
        "vp9" => "VP9",
        "vp8" => "VP8",
        "mpeg2video" => "MPEG-2",
        "mpeg4" => "MPEG-4",
        "vc1" => "VC-1",
        var other => other.ToUpperInvariant()
    };

    private static string FormatAudioCodec(MediaStream stream)
    {
        var codec = stream.Codec?.ToLowerInvariant() ?? string.Empty;
        var profile = stream.Profile ?? string.Empty;
        var isAtmos = profile.Contains("Atmos", StringComparison.OrdinalIgnoreCase)
            || (stream.Title?.Contains("Atmos", StringComparison.OrdinalIgnoreCase) ?? false);

        if (codec.StartsWith("pcm", StringComparison.Ordinal))
        {
            return "PCM";
        }

        return codec switch
        {
            "dts" when profile.Contains("DTS:X", StringComparison.OrdinalIgnoreCase) => "DTS:X",
            "dts" when profile.Contains("MA", StringComparison.Ordinal) => "DTS-HD MA",
            "dts" when profile.Contains("HD", StringComparison.Ordinal) => "DTS-HD",
            "dts" => "DTS",
            "truehd" => isAtmos ? "TrueHD Atmos" : "TrueHD",
            "eac3" => isAtmos ? "EAC3 Atmos" : "EAC3",
            "ac3" => "AC3",
            "aac" => "AAC",
            "flac" => "FLAC",
            "alac" => "ALAC",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "mp3" => "MP3",
            "mp2" => "MP2",
            "wmav2" or "wmapro" => "WMA",
            "" => "?",
            _ => codec.ToUpperInvariant()
        };
    }

    private sealed record CacheEntry(DateTime ItemModified, DateTime CachedAt, ItemBadges Badges);
}
