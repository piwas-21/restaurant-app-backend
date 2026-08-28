namespace RestaurantSystem.Api.Common.Models;

/// <summary>
/// One product a bulk attach did not change, and why — nothing is skipped in silence (plan S8).
/// </summary>
/// <remarks>
/// Shared by the ingredient and the variation attach because the two answer the SAME question with
/// the same two reasons; a second copy of this shape would be a contract that can drift while the
/// screen renders both through one component.
/// </remarks>
public class AttachSkippedProductDto
{
    public Guid ProductId { get; set; }

    /// <summary>Empty when the id resolved to no product at all, which is the point of the reason.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// <c>alreadyLinked</c> or <c>notFound</c>. A rule violation is never here: it refuses the whole
    /// batch with a 400, because nothing is written when one target would end up invalid.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>The two values <see cref="AttachSkippedProductDto.Reason"/> takes, named once.</summary>
public static class AttachSkipReasons
{
    public const string AlreadyLinked = "alreadyLinked";
    public const string NotFound = "notFound";
}
