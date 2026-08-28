namespace RestaurantSystem.Api.Common.Models;

/// <summary>
/// One product that carries a copy of a library row — the drill-down behind "used on N items"
/// (plan S3 shipped the COUNT; plan S8 needs the LIST, because a blast-radius confirm has to say
/// which items are already covered and which the next action would actually change).
/// </summary>
/// <remarks>
/// It carries the product's own <c>IsActive</c> because the count deliberately includes inactive
/// products — the link is real and archiving the library row still affects them — so a screen that
/// showed a bare number could not explain why 41 items include one nobody can order.
/// </remarks>
public class CatalogUsageProductDto
{
    public Guid ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
