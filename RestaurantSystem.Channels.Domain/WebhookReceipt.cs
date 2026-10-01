namespace RestaurantSystem.Channels.Domain;

// Notification metadata only. Neither raw bodies nor customer/order details enter this inbox.
public sealed record WebhookReceipt(
    string ClientId,
    string EventId,
    string EventType,
    Guid StoreId,
    string? ResourceId,
    long EventTime,
    string BodyHash,
    DateTimeOffset ReceivedAt);
