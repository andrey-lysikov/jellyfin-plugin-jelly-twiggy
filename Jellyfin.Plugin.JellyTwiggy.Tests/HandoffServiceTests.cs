using Jellyfin.Plugin.JellyTwiggy.Handoff;
using MediaBrowser.Model.Session;
using Xunit;
using static Jellyfin.Plugin.JellyTwiggy.Tests.HandoffRulesTests;

namespace Jellyfin.Plugin.JellyTwiggy.Tests;

public class HandoffServiceTests
{
    private const long Second = TimeSpan.TicksPerSecond;

    // This browser: idle, remote-controllable, the "Jellyfin Web" client.
    private static readonly HandoffSession Browser = Session("own", deviceId: MyDevice, deviceName: "Браузер", client: "Jellyfin Web", idle: true);

    private sealed class FakeSessions(params HandoffSession[] sessions) : IHandoffSessions
    {
        public List<string> Calls { get; } = [];

        public List<(string Target, PlayRequest Request)> Plays { get; } = [];

        public long? StoppedAt { get; set; }

        public Exception? PlayFails { get; set; }

        public IReadOnlyList<HandoffSession> GetSessions() => sessions;

        public Task SendMessage(string controllingSessionId, string sessionId, string header, string text, long timeoutMs, CancellationToken cancellationToken)
        {
            Calls.Add($"message {sessionId} {header} {text} {timeoutMs}");
            return Task.CompletedTask;
        }

        public Task<long?> StopAndWait(string controllingSessionId, string sessionId, TimeSpan wait, CancellationToken cancellationToken)
        {
            Calls.Add($"stop {sessionId}");
            return Task.FromResult(StoppedAt);
        }

        public Task Stop(string controllingSessionId, string sessionId, CancellationToken cancellationToken)
        {
            Calls.Add($"stop {sessionId}");
            return Task.CompletedTask;
        }

        public Task Play(string controllingSessionId, string sessionId, PlayRequest request, CancellationToken cancellationToken)
        {
            Calls.Add($"play {sessionId}");
            Plays.Add((sessionId, request));
            return PlayFails is null ? Task.CompletedTask : Task.FromException(PlayFails);
        }
    }

    private static HandoffService Service(FakeSessions fake) => new(fake, () => Now);

    [Fact]
    public async Task TakeoverOfSwiftlySendsHintThenStopThenPlaysHere()
    {
        var fake = new FakeSessions(Browser, Session("tv")) { StoppedAt = 605 * Second };
        var result = await Service(fake).TakeOver(Me, MyDevice, "tv", CancellationToken.None);

        Assert.Equal(HandoffStatus.Ok, result.Status);
        Assert.Equal("Fernseher Wohnzimmer", result.DeviceName);
        Assert.Equal(new[] { "message tv Swiftly-Uebernahme Браузер 1000", "stop tv", "play own" }, fake.Calls);
        var play = fake.Plays.Single().Request;
        Assert.Equal(605 * Second, play.StartPositionTicks);
        Assert.Equal("source-1", play.MediaSourceId);
        Assert.Equal(1, play.AudioStreamIndex);
        Assert.Equal(2, play.SubtitleStreamIndex);
    }

    [Fact]
    public async Task TakeoverOfOtherClientTouchesNothing()
    {
        var fake = new FakeSessions(Browser, Session("web", client: "Jellyfin Web"));
        var result = await Service(fake).TakeOver(Me, MyDevice, "web", CancellationToken.None);

        Assert.Equal(HandoffStatus.SourceGone, result.Status);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task TakeoverWithoutStopReportUsesSessionPosition()
    {
        var fake = new FakeSessions(Browser, Session("tv", position: 600 * Second));
        await Service(fake).TakeOver(Me, MyDevice, "tv", CancellationToken.None);

        Assert.Equal(600 * Second, fake.Plays.Single().Request.StartPositionTicks);
    }

    [Fact]
    public async Task TakeoverOfOtherUsersSessionTouchesNothing()
    {
        var fake = new FakeSessions(Browser, Session("anna", userId: Other));
        var result = await Service(fake).TakeOver(Me, MyDevice, "anna", CancellationToken.None);

        Assert.Equal(HandoffStatus.SourceGone, result.Status);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task TakeoverWithoutOwnSessionStopsNothing()
    {
        var fake = new FakeSessions(Session("tv"));
        var result = await Service(fake).TakeOver(Me, MyDevice, "tv", CancellationToken.None);

        Assert.Equal(HandoffStatus.NoOwnSession, result.Status);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task PlayOnSendsPlayNowToFreshestSessionOfDevice()
    {
        var fake = new FakeSessions(Browser, Session("tv-alt", secondsAgo: 50, idle: true), Session("tv-neu", secondsAgo: 1, idle: true));
        var play = new HandoffPlay(Movie, 90 * Second, null, null, -1);
        var result = await Service(fake).PlayOn(Me, MyDevice, "tv-wohnzimmer", play, CancellationToken.None);

        Assert.Equal(HandoffStatus.Ok, result.Status);
        var (target, request) = fake.Plays.Single();
        Assert.Equal("tv-neu", target);
        Assert.Equal(900_000_000, request.StartPositionTicks);
        Assert.Null(request.MediaSourceId);
        Assert.Equal(-1, request.SubtitleStreamIndex);
    }

    [Fact]
    public async Task PlayOnMissingDeviceReportsAndPlaysNothing()
    {
        var fake = new FakeSessions(Browser, Session("tv", secondsAgo: 200));
        var result = await Service(fake).PlayOn(Me, MyDevice, "tv-wohnzimmer", new HandoffPlay(Movie, null, null, null, null), CancellationToken.None);

        Assert.Equal(HandoffStatus.TargetGone, result.Status);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task MoveFromPlayerPlaysThereBeforeStoppingHere()
    {
        var playing = Session("own", deviceId: MyDevice, client: "Jellyfin Web", position: 100 * Second);
        var fake = new FakeSessions(playing, Session("tv", idle: true));
        await Service(fake).MoveFromPlayer(Me, MyDevice, "tv-wohnzimmer", 107 * Second, CancellationToken.None);

        Assert.Equal(new[] { "play tv", "stop own" }, fake.Calls);
        Assert.Equal(107 * Second, fake.Plays.Single().Request.StartPositionTicks);
    }

    [Fact]
    public async Task MoveFromPlayerKeepsPlayingHereWhenTargetFails()
    {
        var playing = Session("own", deviceId: MyDevice, client: "Jellyfin Web");
        var fake = new FakeSessions(playing, Session("tv", idle: true)) { PlayFails = new InvalidOperationException("Gerät antwortet nicht") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(fake).MoveFromPlayer(Me, MyDevice, "tv-wohnzimmer", null, CancellationToken.None));
        Assert.DoesNotContain("stop own", fake.Calls);
    }

    [Fact]
    public async Task MoveSendsHintThenStopsSourceThenPlaysOnTarget()
    {
        var source = Session("tv", position: 300 * Second);
        var target = Session("ipad", deviceId: "ipad-kueche", deviceName: "iPad Küche", idle: true);
        var fake = new FakeSessions(Browser, source, target) { StoppedAt = 304 * Second };
        var result = await Service(fake).Move(Me, MyDevice, "tv", "ipad-kueche", CancellationToken.None);

        Assert.Equal(HandoffStatus.Ok, result.Status);
        Assert.Equal(new[] { "message tv Swiftly-Uebernahme iPad Küche 1000", "stop tv", "play ipad" }, fake.Calls);
        var play = fake.Plays.Single().Request;
        Assert.Equal(304 * Second, play.StartPositionTicks);
        Assert.Equal(1, play.AudioStreamIndex);
        Assert.Equal(2, play.SubtitleStreamIndex);
    }

    [Fact]
    public async Task MoveToMissingDeviceLeavesSourcePlaying()
    {
        var fake = new FakeSessions(Browser, Session("tv"), Session("ipad", deviceId: "ipad-kueche", secondsAgo: 300, idle: true));
        var result = await Service(fake).Move(Me, MyDevice, "tv", "ipad-kueche", CancellationToken.None);

        Assert.Equal(HandoffStatus.TargetGone, result.Status);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task MoveToSameDeviceIsRejected()
    {
        var fake = new FakeSessions(Browser, Session("tv"));
        var result = await Service(fake).Move(Me, MyDevice, "tv", "tv-wohnzimmer", CancellationToken.None);

        Assert.Equal(HandoffStatus.TargetGone, result.Status);
        Assert.Empty(fake.Calls);
    }
}
