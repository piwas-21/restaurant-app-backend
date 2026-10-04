namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed class OrderBillingSnapshotOptions
{
    public const string SectionName = "OrderBillingSnapshot";

    public int MaximumUnitRows { get; set; } = 1_000;
}
