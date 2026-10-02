namespace RestaurantSystem.Channels.Domain;

/// <summary>Authenticated store-list metadata only. Customer/order bodies remain outside this record.</summary>
public sealed record CreatedOrderCandidate(Guid OrderId, DateTimeOffset PlacedAt, string ResponseHash);
