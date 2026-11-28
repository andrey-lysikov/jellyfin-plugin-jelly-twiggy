using Jellyfin.Plugin.JellyTwiggy.Picks;
using Xunit;

namespace Jellyfin.Plugin.JellyTwiggy.Tests;

public class PickCatalogJsonTests
{
    private static string DefaultJson()
    {
        var assembly = typeof(PickCatalog).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("catalog.default.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void DefaultCatalogMatchesBuiltInThemes()
    {
        var catalog = CatalogJson.Deserialize(DefaultJson());

        Assert.Equal(CatalogDto.CurrentVersion, catalog.Version);
        // Every built-in theme has a JSON counterpart, so export and windows stay in sync.
        Assert.Equal(
            PickCatalog.Themes.Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal),
            catalog.Items.Select(c => c.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void CatalogRoundTrips()
    {
        var json = CatalogJson.Serialize(new CatalogDto(
            CatalogDto.CurrentVersion,
            [
                new CategoryDto(
                    "custom-test",
                    "holiday",
                    Name: "Test",
                    Window: new WindowDto("annual", Month: 3, Day: 8),
                    Rule: new RuleDto(StrongTags: ["x"], TitlePattern: "a|b", Threshold: 3),
                    Limit: 10,
                    Minimum: 2),
                new CategoryDto("custom-films", "franchise", Franchise: new FranchiseDto(Studios: ["A24"]))
            ]));
        var parsed = CatalogJson.Deserialize(json);

        Assert.Equal(2, parsed.Items.Count);
        Assert.Equal("custom-test", parsed.Items[0].Id);
        Assert.Equal("a|b", parsed.Items[0].Rule!.TitlePattern);
        Assert.Equal(["A24"], parsed.Items[1].Franchise!.Studios);
    }

    [Fact]
    public void BadJsonIsRejected()
    {
        Assert.Throws<CatalogException>(() => CatalogJson.Deserialize("not json"));
        Assert.Throws<CatalogException>(() => CatalogJson.Deserialize("{\"version\":99,\"categories\":[]}"));
    }
}
