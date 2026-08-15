namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Decides, at creation time, which language a new row's mails will be written in
/// (EMAIL-LOCALISATION-PLAN §2, S4) — the request half of <see cref="IEmailLanguageResolver"/>,
/// plus the one thing that resolver cannot see on its own: the account's stored preference.
/// </summary>
public interface IPreferredLanguageCapture
{
    /// <summary>
    /// The language to freeze on a row created for <paramref name="userId"/>: that account's own
    /// preference if it has one, else what the current request's <c>Accept-Language</c> asks for,
    /// else the tenant default. Never null — a row created outside any request (a webhook, a
    /// background job) gets the tenant default rather than nothing, so the mail that follows has
    /// a language even though the guest never expressed one.
    /// </summary>
    /// <remarks>
    /// A guest order passes <c>null</c> and resolves from the request alone. The stored value is
    /// always canonical, because it comes from the resolver's own supported set — S2's value
    /// converter is a safety net for what is stored, not a substitute for resolving here.
    /// </remarks>
    Task<string> ForUserAsync(Guid? userId, CancellationToken cancellationToken = default);
}
