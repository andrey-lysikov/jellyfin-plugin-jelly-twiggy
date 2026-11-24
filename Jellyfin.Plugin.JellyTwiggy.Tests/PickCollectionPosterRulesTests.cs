using Jellyfin.Plugin.JellyTwiggy.Picks;
using Xunit;

namespace Jellyfin.Plugin.JellyTwiggy.Tests;

public class PickCollectionPosterRulesTests
{
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void SignatureIsStableAndOrderSensitive()
    {
        var signature = PickCollectionPosterRules.Signature([First, Second]);

        Assert.Equal(signature, PickCollectionPosterRules.Signature([First, Second]));
        Assert.NotEqual(signature, PickCollectionPosterRules.Signature([Second, First]));
    }

    [Fact]
    public void SinglePosterDoesNotNeedACollage()
    {
        Assert.Equal(["poster.jpg"], PickCollectionPosterRules.CollagePaths(["poster.jpg"]));
    }

    [Fact]
    public void TwoPostersFillBothColumns()
    {
        Assert.Equal(
            ["first.jpg", "first.jpg", "second.jpg", "second.jpg"],
            PickCollectionPosterRules.CollagePaths(["first.jpg", "second.jpg"]));
    }

    [Fact]
    public void ThreePostersFillEveryCell()
    {
        Assert.Equal(
            ["first.jpg", "second.jpg", "first.jpg", "third.jpg"],
            PickCollectionPosterRules.CollagePaths(["first.jpg", "second.jpg", "third.jpg"]));
    }

    [Fact]
    public void FourPostersUseReadingOrder()
    {
        Assert.Equal(
            ["first.jpg", "third.jpg", "second.jpg", "fourth.jpg"],
            PickCollectionPosterRules.CollagePaths(["first.jpg", "second.jpg", "third.jpg", "fourth.jpg"]));
    }
}
