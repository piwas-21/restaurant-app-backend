using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.DeliveryChannels.Management;
using RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

public sealed class DeliveryChannelManagementClientCatalogueTests
{
    private const string StaleCatalogue = """
        {
          "storeId":"51000000-0000-0000-0000-000000000001",
          "currency":"EUR",
          "mappingRevision":"mapping-r7",
          "draftRevision":"draft-r12",
          "sourceRevision":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
          "draftSourceRevision":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "sourceChanged":true,
          "canPublish":false,
          "items":[],
          "serviceAvailability":[],
          "serviceHoursEditable":false,
          "blockingCodes":["source_changed"],
          "warningCodes":[],
          "latestPublication":null,
          "serviceHoursStatus":"reviewedTemplate",
          "currentServiceAvailability":[],
          "currentServiceHoursStatus":"verified",
          "selectionMode":"categoryItemsV1",
          "categories":[],
          "selectedItems":[{
            "selectionKey":"52000000-0000-0000-0000-000000000001:base",
            "providerItemId":"uber-item-1",
            "productId":"52000000-0000-0000-0000-000000000001",
            "variationId":null,
            "categoryId":"53000000-0000-0000-0000-000000000001",
            "categoryName":"Meals",
            "categoryDisplayOrder":0,
            "itemDisplayOrder":2,
            "name":"Vegetable soup",
            "variationName":null,
            "description":"Soup",
            "priceMinor":1250,
            "available":true,
            "supported":true,
            "blockReason":"",
            "sourceFingerprint":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
          }],
          "taxProfile":null,
          "taxProfileRevision":""
        }
        """;

    [Fact]
    public async Task StaleCatalogueFieldsSurviveGatewayProxyRoundTrip()
    {
        using var http = new HttpClient(new ResponseHandler()) { BaseAddress = new Uri("https://gateway.example/") };
        var client = new DeliveryChannelManagementClient(http,
            Options.Create(new DeliveryChannelManagementSettings
            { Enabled = true, ServerCredential = new string('a', 64), TenantId = "bound-tenant" }),
            Options.Create(new DeliveryChannelSettings
            { Stores = [new() { StoreId = "51000000-0000-0000-0000-000000000001" }] }));

        var catalogue = await client.Send<DeliveryChannelCatalogueDto>(HttpMethod.Get,
            "api/tenant-management/uber/catalogue", Guid.Parse("54000000-0000-0000-0000-000000000001"), null, default);

        Assert.Equal("categoryItemsV1", catalogue.SelectionMode);
        Assert.Equal("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", catalogue.SourceRevision);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", catalogue.DraftSourceRevision);
        Assert.Equal("draft-r12", catalogue.DraftRevision);
        Assert.True(catalogue.SourceChanged);
        Assert.False(catalogue.CanPublish);
        Assert.Equal("source_changed", Assert.Single(catalogue.BlockingCodes));
        Assert.Equal("uber-item-1", Assert.Single(catalogue.SelectedItems).ProviderItemId);

        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(catalogue, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var json = serialized.RootElement;
        Assert.Equal("categoryItemsV1", json.GetProperty("selectionMode").GetString());
        Assert.Equal(catalogue.SourceRevision, json.GetProperty("sourceRevision").GetString());
        Assert.Equal(catalogue.DraftSourceRevision, json.GetProperty("draftSourceRevision").GetString());
        Assert.Equal("draft-r12", json.GetProperty("draftRevision").GetString());
        Assert.True(json.GetProperty("sourceChanged").GetBoolean());
        Assert.False(json.GetProperty("canPublish").GetBoolean());
        Assert.Equal("source_changed", json.GetProperty("blockingCodes")[0].GetString());
        Assert.Equal("uber-item-1", json.GetProperty("selectedItems")[0].GetProperty("providerItemId").GetString());
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/tenant-management/uber/catalogue", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(StaleCatalogue, Encoding.UTF8, "application/json") });
        }
    }
}
