using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Services;

/// <summary>
/// Decides which library id a product write may persist onto
/// <see cref="ProductVariation.GlobalVariationId"/> — the variation twin of
/// <see cref="GlobalIngredientProvenance"/>, and deliberately the same shape, because that shape has
/// already survived two slices and one archive state.
///
/// <para>
/// The link is PROVENANCE, not a shared identity: the admin picked this row out of the library, so
/// the name and its translations were copied from it. Nothing reads the library row afterwards — an
/// edit there does not propagate — and the order line has carried its own frozen variation name
/// since long before this slice (<c>OrderItem.VariationName</c>).
/// </para>
///
/// <para>
/// Why a check at all: <c>global_variation_id</c> is a real FK with <c>NO ACTION</c>, so an id the
/// caller invented would surface as a 500 from <c>SaveChangesAsync</c> — a database error for what
/// is a bad request about an optional decoration. An unknown id is dropped to <c>null</c> with a
/// warning, and the variation still saves.
/// </para>
///
/// <para>
/// The check applies to a link the payload is CHANGING, never to one the row already carries. That
/// distinction is what lets a library row leave the shelf without taking the products with it: a
/// <see cref="GlobalVariation"/> can be archived or soft-deleted, and neither is offered here, so
/// validating an unchanged link would mean that archiving one library entry silently erased the
/// provenance of every product that ever used it, on that product's next save.
/// </para>
/// </summary>
internal sealed class GlobalVariationProvenance
{
    private readonly IReadOnlySet<Guid> _knownIds;
    private readonly ILogger _logger;

    private GlobalVariationProvenance(IReadOnlySet<Guid> knownIds, ILogger logger)
    {
        _knownIds = knownIds;
        _logger = logger;
    }

    /// <summary>
    /// One query for the whole payload — not one per variation — and none at all when no entry
    /// carries a link, which is every save made by a client that predates the picker.
    /// </summary>
    public static async Task<GlobalVariationProvenance> ResolveAsync(
        ApplicationDbContext context,
        IEnumerable<Guid?> requestedLinks,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var requested = requestedLinks.OfType<Guid>().ToHashSet();

        if (requested.Count == 0)
        {
            return new GlobalVariationProvenance(new HashSet<Guid>(), logger);
        }

        var known = await context.GlobalVariations
            // Archived as well as soft-deleted: a row an admin archived is one the picker no longer
            // offers, so a NEW link to it is as unfounded as an invented id.
            .Where(g => requested.Contains(g.Id) && g.ArchivedAt == null)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        return new GlobalVariationProvenance(known.ToHashSet(), logger);
    }

    /// <summary>
    /// The id to persist for one incoming variation: the supplied one when it is either unchanged or
    /// a live library row, otherwise <c>null</c>.
    /// </summary>
    /// <param name="requestedLink">What the payload asks for.</param>
    /// <param name="variationName">Only for the warning — a caller debugging a dropped link needs it.</param>
    /// <param name="currentLink">
    /// What the stored row links to today, on the update path; <c>null</c> for a row being created.
    /// </param>
    public Guid? LinkFor(Guid? requestedLink, string variationName, Guid? currentLink = null)
    {
        if (!requestedLink.HasValue)
        {
            return null;
        }

        var id = requestedLink.Value;
        // Unchanged: the FK held when this was written, so there is nothing to prove — and asking
        // would drop the link the moment the library row is archived.
        if (id == currentLink || _knownIds.Contains(id))
        {
            return id;
        }

        _logger.LogWarning(
            "Global variation {GlobalVariationId} does not exist; saving variation {VariationName} without provenance",
            id, variationName);
        return null;
    }
}
