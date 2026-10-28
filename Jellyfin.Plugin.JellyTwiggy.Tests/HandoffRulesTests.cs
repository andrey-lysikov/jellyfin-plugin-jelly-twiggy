using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyTwiggy.Handoff;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Plugin.JellyTwiggy.Tests;

public class HandoffRulesTests
{
    internal static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    internal static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal static readonly Guid Movie = Guid.Parse("33333333-3333-3333-3333-333333333333");
    internal const string MyDevice = "browser-ru";
    private const long Second = TimeSpan.TicksPerSecond;

    // A session of the caller on another device, playing a film: fits takeover and "Play on" (as Swiftly).
    internal static HandoffSession Session(
        string id = "s1",
        Guid? userId = null,
        string? deviceId = "tv-wohnzimmer",
        string? deviceName = "Fernseher Wohnzimmer",
        string? client = "Swiftly",
        bool remote = true,
        bool video = true,
        Guid? itemId = null,
        BaseItemKind? type = BaseItemKind.Movie,
        long? position = 600 * Second,
        double secondsAgo = 5,
        bool idle = false) => new(
            id,
            userId ?? Me,
            deviceId,
            deviceName,
            client,
            remote,
            video,
            idle ? null : itemId ?? Movie,
            idle ? null : type,
            position,
            "source-1",
            1,
            2,
            Now.AddSeconds(-secondsAgo));

    private static string? WhyNot(HandoffSession s) => HandoffRules.WhyNotTakeover(s, Me, MyDevice, Now);

    [Fact]
    public void TakeoverFits() => Assert.Null(WhyNot(Session()));

    [Fact]
    public void TakeoverRejectsOwnDevice() => Assert.Equal("self", WhyNot(Session(deviceId: MyDevice)));

    [Fact]
    public void TakeoverRejectsOtherUser() => Assert.Equal("user", WhyNot(Session(userId: Other)));

    [Fact]
    public void TakeoverRejectsIdleSession() => Assert.Equal("idle", WhyNot(Session(idle: true)));

    [Fact]
    public void TakeoverRejectsEmptyItemId() => Assert.Equal("idle", WhyNot(Session(itemId: Guid.Empty)));

    [Theory]
    [InlineData(BaseItemKind.Audio)]
    [InlineData(BaseItemKind.AudioBook)]
    [InlineData(BaseItemKind.MusicAlbum)]
    [InlineData(BaseItemKind.Playlist)]
    public void TakeoverRejectsAudio(BaseItemKind type) => Assert.Equal("audio", WhyNot(Session(type: type)));

    [Fact]
    public void TakeoverRejectsSwiftlyMusic() => Assert.Equal("audio", WhyNot(Session(client: "Swiftly Music")));

    [Fact]
    public void TakeoverRejectsNoRemoteControl() => Assert.Equal("remote", WhyNot(Session(remote: false)));

    [Fact]
    public void TakeoverRejectsStaleSession() => Assert.Equal("stale", WhyNot(Session(secondsAgo: 91)));

    [Fact]
    public void TakeoverRejectsOtherClient() => Assert.Equal("client", WhyNot(Session(client: "Jellyfin Web")));

    [Fact]
    public void TakeoverAcceptsSessionAtFreshnessLimit() => Assert.Null(WhyNot(Session(secondsAgo: 90)));

    [Fact]
    public void TakeoverChecksOwnDeviceFirst() => Assert.Equal("self", WhyNot(Session(deviceId: MyDevice, userId: Other, remote: false)));

    [Fact]
    public void TakeoverIgnoresOtherUsersForAdmins()
    {
        var sessions = new[] { Session("a", userId: Other, deviceId: "tv-anna"), Session("b") };
        Assert.Equal(new[] { "b" }, HandoffRules.TakeoverSources(sessions, Me, MyDevice, Now).Select(s => s.Id));
    }

    [Fact]
    public void TakeoverListsSeveralFreshestFirst()
    {
        var sessions = new[]
        {
            Session("old", deviceId: "ipad", secondsAgo: 60),
            Session("new", deviceId: "tv", secondsAgo: 2),
            Session("mid", deviceId: "mac", secondsAgo: 20)
        };
        Assert.Equal(new[] { "new", "mid", "old" }, HandoffRules.TakeoverSources(sessions, Me, MyDevice, Now).Select(s => s.Id));
    }

    [Fact]
    public void StartPointWithoutReportUsesSession() => Assert.Equal(600 * Second, HandoffRules.StartPoint(500 * Second, null, 600 * Second));

    [Fact]
    public void StartPointWithUnchangedReportUsesSession() => Assert.Equal(600 * Second, HandoffRules.StartPoint(500 * Second, 500 * Second, 600 * Second));

    [Fact]
    public void StartPointReportFarBehindUsesSession() => Assert.Equal(600 * Second, HandoffRules.StartPoint(500 * Second, 0, 600 * Second));

    [Fact]
    public void StartPointReportAheadWins() => Assert.Equal(608 * Second, HandoffRules.StartPoint(500 * Second, 608 * Second, 600 * Second));

    [Fact]
    public void StartPointReportWithinOneSecondBehindWins() => Assert.Equal(599 * Second, HandoffRules.StartPoint(null, 599 * Second, 600 * Second));

    private static string? WhyNotTarget(HandoffSession s) => HandoffRules.WhyNotTarget(s, Me, MyDevice, Now);

    [Fact]
    public void TargetFitsIdleSwiftly() => Assert.Null(WhyNotTarget(Session(idle: true)));

    [Fact]
    public void TargetRejectsOwnDevice() => Assert.Equal("self", WhyNotTarget(Session(deviceId: MyDevice)));

    [Fact]
    public void TargetRejectsOtherUser() => Assert.Equal("user", WhyNotTarget(Session(userId: Other)));

    [Fact]
    public void TargetRejectsNoRemoteControl() => Assert.Equal("remote", WhyNotTarget(Session(remote: false)));

    [Fact]
    public void TargetRejectsNoVideo() => Assert.Equal("video", WhyNotTarget(Session(video: false)));

    [Fact]
    public void TargetRejectsStaleSession() => Assert.Equal("stale", WhyNotTarget(Session(secondsAgo: 120)));

    [Fact]
    public void TargetRejectsSwiftlyMusic() => Assert.Equal("client", WhyNotTarget(Session(client: "Swiftly Music")));

    [Fact]
    public void TargetRejectsOtherClient() => Assert.Equal("client", WhyNotTarget(Session(client: "Jellyfin Web")));

    [Fact]
    public void TargetsKeepFreshestSessionPerDevice()
    {
        var sessions = new[]
        {
            Session("tv-alt", deviceId: "tv", secondsAgo: 40),
            Session("tv-neu", deviceId: "tv", secondsAgo: 3),
            Session("ipad", deviceId: "ipad", secondsAgo: 10)
        };
        Assert.Equal(new[] { "tv-neu", "ipad" }, HandoffRules.Targets(sessions, Me, MyDevice, Now).Select(s => s.Id));
    }

    [Fact]
    public void IsSwiftlyMatchesPrefixButNotMusic()
    {
        Assert.True(HandoffRules.IsSwiftly("Swiftly"));
        Assert.True(HandoffRules.IsSwiftly("Swiftly tvOS"));
        Assert.False(HandoffRules.IsSwiftly("Swiftly Music"));
        Assert.False(HandoffRules.IsSwiftly("Jellyfin Web"));
        Assert.False(HandoffRules.IsSwiftly(null));
    }

    [Fact]
    public void PlayNowCarriesOnlyWhatToPlay()
    {
        var request = HandoffRules.BuildPlay(Movie, 90 * Second, "source-1", 1, -1);
        Assert.Equal(PlayCommand.PlayNow, request.PlayCommand);
        Assert.Equal(new[] { Movie }, request.ItemIds);
        Assert.Equal(900_000_000, request.StartPositionTicks);
        Assert.Equal("source-1", request.MediaSourceId);
        Assert.Equal(1, request.AudioStreamIndex);
        Assert.Equal(-1, request.SubtitleStreamIndex);
    }

    [Fact]
    public void PlayNowLeavesOptionalFieldsOut()
    {
        var request = HandoffRules.BuildPlay(Movie, null, null, null, null);
        Assert.Null(request.StartPositionTicks);
        Assert.Null(request.MediaSourceId);
        Assert.Null(request.AudioStreamIndex);
        Assert.Null(request.SubtitleStreamIndex);
    }

    [Fact]
    public void PlayNowDropsZeroStart() => Assert.Null(HandoffRules.BuildPlay(Movie, 0, null, null, null).StartPositionTicks);
}
