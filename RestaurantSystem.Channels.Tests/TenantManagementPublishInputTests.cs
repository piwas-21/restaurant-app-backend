using System.Text.Json;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantManagementPublishInputTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TaxConfirmationIsOptionalForFixedModeAndExplicitForCategoryMode()
    {
        var omitted = JsonSerializer.Deserialize<TenantManagementPublishRequest>(
            "{\"draftRevision\":\"draft\",\"publicationRevision\":\"publication\"}", Options);
        var explicitFalse = JsonSerializer.Deserialize<TenantManagementPublishRequest>(
            "{\"draftRevision\":\"draft\",\"publicationRevision\":\"publication\",\"confirmedTaxProfile\":false}", Options);

        Assert.Null(omitted!.ConfirmedTaxProfile);
        Assert.False(explicitFalse!.ConfirmedTaxProfile);
    }
}
