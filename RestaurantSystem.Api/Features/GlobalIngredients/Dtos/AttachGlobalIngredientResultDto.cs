using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

/// <summary>
/// What the bulk attach did, itemised — the receipt for a write the admin cannot see the effect of
/// without opening forty products.
/// </summary>
public class AttachGlobalIngredientResultDto
{
    /// <summary>
    /// The library row's own kind, echoed so the client can say WHICH group the rows landed in
    /// without a second request. It is the catalog row that decides, not the caller.
    /// </summary>
    public IngredientKind Kind { get; set; }

    public List<Guid> AttachedProductIds { get; set; } = [];

    public List<AttachSkippedProductDto> Skipped { get; set; } = [];
}
