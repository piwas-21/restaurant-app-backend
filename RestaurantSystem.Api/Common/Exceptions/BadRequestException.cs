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
}
