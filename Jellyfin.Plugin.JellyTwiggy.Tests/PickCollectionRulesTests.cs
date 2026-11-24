using Jellyfin.Plugin.JellyTwiggy.Picks;
using Xunit;

namespace Jellyfin.Plugin.JellyTwiggy.Tests;

public class PickCollectionRulesTests
{
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Deleted = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void TitleFallsBackWithoutPicks()
    {
        Assert.Equal("Coming soon!", PickCollectionRules.Title("Coming soon!", null));
        Assert.Equal("Coming soon!", PickCollectionRules.Title("Coming soon!", new PickResult()));
    }

    [Fact]
    public void TitleJoinsDistinctGroupNames()
    {
        var result = Result(
            Group("newyear", "New Year", First),
            Group("franchise", "Marvel", Second),
            Group("duplicate", "Marvel", Deleted));

        Assert.Equal("New Year • Marvel", PickCollectionRules.Title("Coming soon!", result));
    }

    [Fact]
    public void PreviousPickLosesOnlyDeletedItems()
    {
        var previous = Result(Group("newyear", "New Year", First, Deleted, Second));

        var retained = PickCollectionRules.RetainPrevious(previous, new HashSet<Guid> { First, Second }, new HashSet<string>());

        Assert.NotNull(retained);
        Assert.Equal(new[] { First, Second }, retained.Groups.Single().ItemIds);
        Assert.Equal(previous.GeneratedUtc, retained.GeneratedUtc);
        Assert.Equal(previous.Date, retained.Date);
        Assert.Equal(new[] { First, Deleted, Second }, previous.Groups.Single().ItemIds);
    }

    [Fact]
    public void PreviousPickDropsDisabledAndEmptyGroups()
    {
        var previous = Result(
            Group("newyear", "New Year", First),
            Group("disabled", "Disabled", Second));

        var retained = PickCollectionRules.RetainPrevious(previous, new HashSet<Guid> { First, Second }, new HashSet<string> { "disabled" });

        Assert.Equal("newyear", Assert.Single(retained!.Groups).PickId);
        Assert.Equal(new[] { First }, retained.Groups[0].ItemIds);
    }

    [Fact]
    public void PreviousPickIsNotCreatedOrRetainedEmpty()
    {
        Assert.Null(PickCollectionRules.RetainPrevious(null, new HashSet<Guid> { First }, new HashSet<string>()));
        Assert.Null(PickCollectionRules.RetainPrevious(
            Result(Group("newyear", "New Year", Deleted)),
            new HashSet<Guid> { First },
            new HashSet<string>()));
    }

    private static PickGroup Group(string id, string name, params Guid[] items) => new()
    {
        PickId = id,
        Name = name,
        ItemIds = [.. items]
    };

    private static PickResult Result(params PickGroup[] groups) => new()
    {
        Groups = [.. groups],
        GeneratedUtc = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc),
        Date = new DateOnly(2026, 11, 24)
    };
}
