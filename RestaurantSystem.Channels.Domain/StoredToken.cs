namespace RestaurantSystem.Channels.Domain;

public sealed record StoredToken(string ClientId, Guid StoreId, string Kind, string Cipher, DateTimeOffset ExpiresAt);
