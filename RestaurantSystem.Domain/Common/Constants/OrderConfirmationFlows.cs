namespace RestaurantSystem.Domain.Common.Constants;

/// <summary>
/// The order-confirmation flows a tenant can pick per order type (admin → settings → order types).
/// <see cref="Direct"/> is the shipped behaviour: the kitchen starts immediately and the guest's
/// "order received" mail says the order is pending confirmation. <see cref="Acknowledge"/> inserts
/// an explicit review step: the guest is told the order was received and is under review for a
/// bounded window, and a cashier approves it — with a preparation time — before the kitchen starts.
/// The values are the persisted column values; renaming them is a migration, not a refactor.
/// </summary>
public static class OrderConfirmationFlows
{
    public const string Direct = "direct";
    public const string Acknowledge = "acknowledge";

    public static readonly IReadOnlyList<string> All = [Direct, Acknowledge];

    public static bool IsValid(string? flow) =>
        flow is not null && All.Contains(flow, StringComparer.Ordinal);
}
