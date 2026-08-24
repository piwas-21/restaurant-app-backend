namespace RestaurantSystem.Api.Features.Auth.Dtos;

/// <summary>
/// The claims of an Apple identity token whose signature, issuer, audience and lifetime have
/// all been verified. Nothing in here is trusted before <c>IAppleIdentityTokenVerifier</c>
/// has produced it.
/// </summary>
/// <param name="Subject">Apple's stable per-app user id (<c>sub</c>).</param>
/// <param name="Email">The address Apple released to this app, if any.</param>
/// <param name="EmailVerified">Apple's own verification flag for <paramref name="Email"/>.</param>
/// <param name="Nonce">
/// The <c>nonce</c> claim when the client sent one. Carried, not enforced: the login command
/// does not transport the raw nonce, so there is nothing to compare it against yet.
/// </param>
public sealed record AppleIdentity(string Subject, string? Email, bool EmailVerified, string? Nonce);
