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
    /// The EFFECTIVE kind — what the rows were actually written with, so the client can say WHICH
    /// group they landed in without a second request.
    /// </summary>
    /// <remarks>
    /// It echoes <c>body.Kind</c> when the caller stated one and the library row's kind otherwise,
    /// which is the same expression <see cref="Services.GlobalIngredientAttach.CopyOnto"/> writes.
    /// It is deliberately not "what you asked for": a caller that omitted the field learns from the
    /// receipt where its rows went, and a caller that stated one gets its own answer back unchanged.
    /// </remarks>
    public IngredientKind Kind { get; set; }

    public List<Guid> AttachedProductIds { get; set; } = [];

    public List<AttachSkippedProductDto> Skipped { get; set; } = [];
}
