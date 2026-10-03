using System.Text.Json;
using RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

public sealed class DeliveryChannelCategoryOverrideInputTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void HumanOverrideRequiresExplicitSelectedValue()
    {
        const string identity = "\"productId\":\"11111111-1111-1111-1111-111111111111\",\"variationId\":null,\"categoryId\":\"22222222-2222-2222-2222-222222222222\"";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DeliveryChannelItemOverrideDto>(
            $"{{{identity}}}", Options));
        var explicitFalse = JsonSerializer.Deserialize<DeliveryChannelItemOverrideDto>(
            $"{{{identity},\"selected\":false}}", Options);

        Assert.NotNull(explicitFalse);
        Assert.False(explicitFalse.Selected);
    }

    [Fact]
    public void HumanPublishContractDistinguishesMissingTaxConfirmationFromFalse()
    {
        var omitted = JsonSerializer.Deserialize<DeliveryChannelPublishRequest>(
            "{\"draftRevision\":\"draft\",\"publicationRevision\":\"publication\"}", Options);
        var explicitFalse = JsonSerializer.Deserialize<DeliveryChannelPublishRequest>(
            "{\"draftRevision\":\"draft\",\"publicationRevision\":\"publication\",\"confirmedTaxProfile\":false}", Options);

        Assert.Null(omitted!.ConfirmedTaxProfile);
        Assert.False(explicitFalse!.ConfirmedTaxProfile);
    }
}
