namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Answers "which language is this mail written in?" — the resolution chain of
/// EMAIL-LOCALISATION-PLAN §1, in one place, for all 15 tenant mails.
/// </summary>
/// <remarks>
/// A language is always a <em>value that is passed</em>, never an ambient one: nothing here
/// reads or writes <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>. Most mail
/// leaves the process on a path with no request at all — the detached order tasks, the Stripe
/// settlement webhook, every BackgroundService — where an ambient culture is silently unset and
/// an ambient design renders English while every test passes (§6.1).
/// </remarks>
public interface IEmailLanguageResolver
{
    /// <summary>
    /// Languages this tenant sells in, in configured order, normalised. Never empty: an
    /// unconfigured instance (the legacy RUMI install) supports all ten.
    /// </summary>
    IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>
    /// §1 rank 4 — the tenant's own language. Always a member of <see cref="SupportedLanguages"/>.
    /// This, and never the guest's, is what operator alerts (M14/M15) and detached jobs use: a
    /// restaurant must not read its own order alerts in whatever language the guest happened to
    /// browse in.
    /// </summary>
    string TenantDefault { get; }

    /// <summary>
    /// The full chain: <paramref name="entityLanguage"/> (rank 1, frozen on the order or
    /// reservation) -> <paramref name="userLanguage"/> (rank 2, the account's preference) ->
    /// <paramref name="requestLanguage"/> (rank 3) -> <see cref="TenantDefault"/> (rank 4) ->
    /// <c>en</c> (rank 5). Unsupported and malformed values are treated as absent and fall
    /// through, so a guest whose language nobody translated still gets a mail. Never returns
    /// null or empty.
    /// </summary>
    /// <remarks>
    /// <paramref name="requestLanguage"/> is a REQUIRED argument with no default, and this type
    /// deliberately does not reach for the ambient request itself. §1 rank 3 is "the mail sent
    /// inside the ORIGINATING request", and most mail is not: the reservation quick-action links
    /// are clicked by the restaurant in the restaurant's browser, an admin status change mails the
    /// guest from a staff request, and the operator alerts (M14/M15) must never follow a guest at
    /// all. An implicit rank 3 would mail a guest in whatever language the staff browser asked for
    /// — and every pre-S4 row has a null language, so that would have been the common case, not the
    /// edge one. Pass <c>null</c> unless the caller IS the guest's own request; S4's capture is the
    /// only place that passes <see cref="FromRequest"/>.
    /// </remarks>
    string Resolve(string? entityLanguage, string? userLanguage, string? requestLanguage);

    /// <summary>
    /// §1 rank 3 alone — the best supported language the current request asks for, or null when
    /// there is no request, no header, or nothing in it this tenant sells in.
    /// </summary>
    /// <remarks>
    /// ONLY valid synchronously on the request's own thread, and only when that request is the
    /// guest's. <c>IHttpContextAccessor</c> is an <c>AsyncLocal</c> and <c>ExecutionContext</c>
    /// flows into <c>Task.Run</c>, so a detached mail task (<c>GuestOrderReceiptSender</c>,
    /// <c>AdminOrderAlertSender</c>) still sees the queueing request's context until the framework
    /// clears it — the same mail would then resolve to the guest's header or to the tenant default
    /// depending on whether the send won the race with the response, and reading a completed
    /// request's headers touches a recycled feature collection. Read this at queue time and pass
    /// the VALUE onward; never call it from inside a queued task.
    /// </remarks>
    string? FromRequest();
}
