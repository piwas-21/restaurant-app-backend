namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

/// <summary>Only claims from a validated provider token; email is contact, never its binding key.</summary>
public sealed record NativeSocialIdentity(string Provider, string Issuer, string Subject,
    string Audience, string? Email, bool EmailVerified);
