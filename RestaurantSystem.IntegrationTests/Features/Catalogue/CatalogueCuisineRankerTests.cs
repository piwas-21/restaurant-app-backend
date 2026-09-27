using System.Text.Json;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueCuisineRankerTests
{
    [Fact]
    public void RankPage_PutsPreferredCuisinesFirstAndKeepsCentralOrderForTies()
    {
        using var source = JsonDocument.Parse("""
            {
              "items": [
                { "templateId": "first-unmatched", "cuisines": ["greek"] },
                { "templateId": "preferred-first", "cuisines": ["turkish"] },
                { "templateId": "preferred-second", "cuisines": ["turkish", "kebab"] },
                { "templateId": "unmatched-second", "cuisines": ["italian"] }
              ],
              "nextCursor": "opaque-cursor"
            }
            """);

        var ranked = CatalogueCuisineRanker.RankPage(source.RootElement, ["turkish", "kebab"]);
        var ids = ranked.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("templateId").GetString()!).ToArray();

        Assert.Equal(
            ["preferred-first", "preferred-second", "first-unmatched", "unmatched-second"],
            ids);
        Assert.Equal("opaque-cursor", ranked.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public void RankPage_WithoutPreferencesPreservesTheCentralResponse()
    {
        using var source = JsonDocument.Parse("""
            { "items": [{ "templateId": "central-first" }, { "templateId": "central-second" }], "nextCursor": null }
            """);

        var ranked = CatalogueCuisineRanker.RankPage(source.RootElement, []);

        Assert.Equal(source.RootElement.GetRawText(), ranked.GetRawText());
    }
}
