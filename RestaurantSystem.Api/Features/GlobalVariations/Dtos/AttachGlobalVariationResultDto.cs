using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.Api.Features.GlobalVariations.Dtos;

/// <summary>
/// What the bulk variation attach did, itemised — the receipt for a write whose effect the admin
/// cannot otherwise see without opening forty products.
/// </summary>
/// <remarks>
/// It carries no <c>Kind</c>, unlike its ingredient twin: a variation has no group discriminator,
/// so there is nothing for the catalog row to decide on the client's behalf.
/// </remarks>
public class AttachGlobalVariationResultDto
{
    public List<Guid> AttachedProductIds { get; set; } = [];

    public List<AttachSkippedProductDto> Skipped { get; set; } = [];
}
