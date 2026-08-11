namespace RestaurantSystem.Api.Common.Exceptions;

public class BadRequestException : Exception
{
    public BadRequestException() : base("Bad request")
    {
    }

    public BadRequestException(string message) : base(message)
    {
    }

    public BadRequestException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Optional stable discriminator surfaced as <c>ApiResponse.ErrorCode</c>, from
    /// <see cref="Common.Models.ErrorCodes"/>.
    /// </summary>
    /// <remarks>
    /// Exists so a client can tell ONE rejection apart from every other 400 without
    /// substring-matching an English sentence. That matters wherever the client re-displays the
    /// message: without a code it can only choose between "show every 400's message" (which leaks
    /// "Session ID is required" and the generic "Validation failed" wrapper to a guest) and "show
    /// none of them" (which throws away the actionable reason). Default <c>null</c> — an
    /// uncoded BadRequestException behaves exactly as before.
    /// </remarks>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// The individual reasons behind this refusal, one entry per broken rule, surfaced as
    /// <c>ApiResponse.Errors</c>. Default <c>null</c> — an exception without it behaves exactly as
    /// before, with <c>Errors</c> carrying the single detail string.
    /// </summary>
    /// <remarks>
    /// Exists because <c>Message</c> can only ever be ONE sentence, and validation routinely has
    /// several. <c>ValidationBehavior</c> joins its failures with "; " into that one sentence, which
    /// is right for a human reading a banner and wrong for the client: <c>apiFormErrors.ts</c>
    /// routes each <c>errors[]</c> entry onto its own form field by matching the text, so a single
    /// joined blob is claimed entirely by the first pattern that matches it — a registration failing
    /// on BOTH password and email filed the whole string under `password` and left the email field
    /// silent (issue #291).
    ///
    /// <c>Message</c> keeps the joined string, so nothing that reads it loses information; this only
    /// splits what was always there. Setting it does NOT change the status code or the message.
    ///
    /// ⚠️ It is NOT additive at the client, and calling it so was wrong. The frontend prefers
    /// <c>errors[]</c> over <c>message</c> on purpose (<c>apiFormErrors.ts</c>: a controller's own
    /// one-argument <c>ApiResponse.Failure</c> leaves <c>message</c> at the literal "Operation
    /// failed"), and **24 call sites read <c>serverMessages(x)[0]</c> — the first entry only**. On
    /// those, a multi-rule refusal used to render the whole joined blob and now renders just the
    /// first rule. Form surfaces that route per-field get strictly better, which is the point;
    /// those 24 get worse until the frontend joins instead of taking <c>[0]</c>. Tracked in
    /// frontend #490, and that fix must ship BEFORE OR WITH this backend release.
    /// </remarks>
    public IReadOnlyList<string>? Errors { get; init; }
}
