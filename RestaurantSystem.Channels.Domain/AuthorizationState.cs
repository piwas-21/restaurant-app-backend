namespace RestaurantSystem.Channels.Domain;

public sealed record AuthorizationState(string Hash, string SessionHash, Guid StoreId, string VerifierCipher,
    DateTimeOffset ExpiresAt, bool EnableTesting = false);
