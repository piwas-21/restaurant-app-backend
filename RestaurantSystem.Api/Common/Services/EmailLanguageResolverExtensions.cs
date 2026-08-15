using System.Globalization;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// The three shapes a send path actually needs from <see cref="IEmailLanguageResolver"/>
/// (EMAIL-LOCALISATION-PLAN §5 S5), as cultures the templates can take.
/// </summary>
/// <remarks>
/// <para>
/// None of these accepts a request language, and that is the whole point. §6.10 is the trap this
/// slice would otherwise walk into: <c>IHttpContextAccessor</c> is an <c>AsyncLocal</c> and
/// <c>ExecutionContext</c> flows into <c>Task.Run</c>, so a detached sender still sees the
/// queueing request's headers — and most tenant mail is queued from a request that is NOT the
/// guest's (a staff status change, the restaurant clicking a quick-action link in its own
/// browser, a Stripe webhook). Rank 3 belongs to <see cref="IPreferredLanguageCapture"/>, which
/// runs on the guest's own write request and freezes the answer on the row; from then on every
/// mail reads that frozen value through <see cref="ForGuest"/>.
/// </para>
/// <para>
/// A caller that wants the request's language therefore cannot get it here by accident: it has to
/// call <see cref="IEmailLanguageResolver.Resolve"/> itself and say so.
/// </para>
/// </remarks>
public static class EmailLanguageResolverExtensions
{
    /// <summary>
    /// The language a guest's mail about a row is written in: the language frozen on that order or
    /// reservation (§1 rank 1), else the tenant's (rank 4). Null is normal — every row written
    /// before S4 has none — and falls through rather than failing.
    /// </summary>
    /// <remarks>
    /// Rank 2 is deliberately skipped here (§1, amended by S5). Capture never leaves rank 1 empty on
    /// a row written since S4, so the account's preference could only ever apply to the legacy
    /// corpus — and none of these send paths holds the user row, so honouring it would mean a
    /// database read per mail on the order-creation path. A pre-S4 order therefore mails in the
    /// tenant's language even if its owner later sets a profile preference.
    /// </remarks>
    public static CultureInfo ForGuest(this IEmailLanguageResolver resolver, string? entityLanguage)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return EmailCultures.For(resolver.Resolve(entityLanguage, userLanguage: null, requestLanguage: null));
    }

    /// <summary>
    /// The language an account's own mail is written in — verification, password reset, welcome,
    /// deletion (§1 rank 2). The account is the recipient here, so its stored preference is the
    /// whole answer; whoever happens to be making the request is not.
    /// </summary>
    public static CultureInfo ForAccount(this IEmailLanguageResolver resolver, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(user);

        return EmailCultures.For(resolver.Resolve(entityLanguage: null, user.PreferredLanguage, requestLanguage: null));
    }

    /// <summary>
    /// The tenant's own language (§1 rank 4) — the operator alerts M14/M15, and any mail whose
    /// recipient is the restaurant. A restaurant must not read its own new-order alert in whatever
    /// language the diner happened to browse in.
    /// </summary>
    public static CultureInfo ForOperator(this IEmailLanguageResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return EmailCultures.For(resolver.TenantDefault);
    }
}
