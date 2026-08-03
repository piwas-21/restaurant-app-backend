namespace RestaurantSystem.Api.Common.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException() : base("The requested resource was not found")
    {
    }

    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string name, object key)
        : base($"Entity '{name}' with ID '{key}' was not found.")
    {
    }

    /// <summary>
    /// Mirrors <see cref="BadRequestException(string, string)"/>: a 404 that a client must be able
    /// to tell apart from every other 404 on the same endpoint.
    /// </summary>
    public NotFoundException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Optional stable discriminator surfaced as <c>ApiResponse.ErrorCode</c>, from
    /// <see cref="Common.Models.ErrorCodes"/>.
    /// </summary>
    /// <remarks>
    /// Exists because ONE endpoint can 404 for two different reasons, and the status code alone
    /// cannot separate them. <c>PUT|DELETE /api/basket/items/{id}</c> answers both "that item is
    /// gone" and "the whole basket row is gone" with a 404, and the two demand opposite client
    /// behaviour: the first is a benign resync (the guest removed it in another tab), the second is
    /// a real failure that must be shown. The frontend used to separate them by substring-matching
    /// the English message, and <c>"Basket not found".Contains("not found")</c> made a basket-level
    /// failure read as the benign case — one tap silently emptied the guest's cart
    /// (frontend issue #415). Default <c>null</c> — an uncoded NotFoundException behaves exactly as
    /// before.
    /// </remarks>
    public string? ErrorCode { get; init; }
}
