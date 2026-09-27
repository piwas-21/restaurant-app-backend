using System.Text.Json;
using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueRequestRequiredFieldsTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("{\"templateId\":\"tr-kofte\",\"locale\":\"tr\",\"idempotencyKey\":\"key\",\"createNewCopy\":false}")]
    [InlineData("{\"templateId\":\"tr-kofte\",\"revision\":1,\"locale\":\"tr\",\"idempotencyKey\":\"key\"}")]
    public void Create_request_rejects_missing_revision_or_copy_choice(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CreateCatalogueImportSessionRequest>(json, WebOptions));
    }

    [Fact]
    public void Create_request_accepts_an_explicit_false_copy_choice()
    {
        var request = JsonSerializer.Deserialize<CreateCatalogueImportSessionRequest>(
            "{\"templateId\":\"tr-kofte\",\"revision\":1,\"locale\":\"tr\",\"idempotencyKey\":\"key\",\"createNewCopy\":false}",
            WebOptions);

        Assert.NotNull(request);
        Assert.False(request.CreateNewCopy);
    }

    [Fact]
    public void Import_request_rejects_a_missing_expected_version()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ImportCatalogueSessionRequest>("""{"idempotencyKey":"key"}""", WebOptions));
    }

    [Fact]
    public void Update_request_rejects_a_missing_expected_version()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateCatalogueImportSessionRequest>("""{"selectedTemplateIds":[]}""", WebOptions));
    }
}
