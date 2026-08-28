using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Services;

/// <summary>
/// Decides which <see cref="ProductIngredientDto.GlobalIngredientId"/> values a product write may
/// persist onto <see cref="ProductIngredient.GlobalIngredientId"/>.
///
/// <para>
/// The link is PROVENANCE, not a shared identity: the admin picked this row out of the global
/// library, so the name and the nine translations were copied from it. Nothing reads the global row
/// afterwards — an edit there does not propagate, and the order line never renders from it (S0n
/// made the order read the per-product name). It exists so a later slice can measure real reuse and
/// then turn propagation on safely.
/// </para>
///
/// <para>
/// Why a check at all: <c>global_ingredient_id</c> is a real FK with <c>NO ACTION</c>, so an id the
/// caller invented would surface as a 500 from <c>SaveChangesAsync</c> — a database error for what
/// is a bad request about an optional decoration. An unknown id is therefore dropped to
/// <c>null</c> with a warning, and the ingredient still saves.
/// </para>
///
/// <para>
/// The check applies to a link the payload is CHANGING, never to one the row already carries. That
/// distinction is what lets a library row leave the shelf without taking the products with it: a
/// <see cref="GlobalIngredient"/> can be archived (S3) or soft-deleted, and neither is offered
/// here, so validating an unchanged link would mean that archiving one library entry silently
/// erased the provenance of every product that ever used it, on that product's next save. An id
/// that is already persisted needs no proof — the FK held when it was written — so it is carried
/// forward untouched, while a NEW link may only point at a live, non-archived row, which is also
/// all the picker ever offers.
/// </para>
/// </summary>
internal sealed class GlobalIngredientProvenance
{
    private readonly IReadOnlySet<Guid> _knownIds;
    private readonly ILogger _logger;

    private GlobalIngredientProvenance(IReadOnlySet<Guid> knownIds, ILogger logger)
    {
        _knownIds = knownIds;
        _logger = logger;
    }

    /// <summary>
    /// One query for the whole payload — not one per ingredient — and none at all when no entry
    /// carries a link, which is every save made by a client that predates the picker.
    /// </summary>
    public static async Task<GlobalIngredientProvenance> ResolveAsync(
        ApplicationDbContext context,
        IEnumerable<ProductIngredientDto> incoming,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var requested = incoming
            .Select(i => i.GlobalIngredientId)
            .OfType<Guid>()
            .ToHashSet();

        if (requested.Count == 0)
        {
            return new GlobalIngredientProvenance(new HashSet<Guid>(), logger);
        }

        var known = await context.GlobalIngredients
            // Archived as well as soft-deleted: S3 gave the catalog a second, reversible way to be
            // off the shelf, and the query filter only hides the first one. A row an admin archived
            // is one the picker no longer offers, so a NEW link to it is as unfounded as an
            // invented id.
            .Where(g => requested.Contains(g.Id) && g.ArchivedAt == null)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        return new GlobalIngredientProvenance(known.ToHashSet(), logger);
    }

    /// <summary>
    /// The id to persist for one incoming ingredient: the supplied one when it is either unchanged
    /// or a live library row, otherwise <c>null</c>.
    /// </summary>
    /// <param name="ingredient">The incoming ingredient.</param>
    /// <param name="currentLink">
    /// What the stored row links to today, on the update path; <c>null</c> for a row being created.
    /// </param>
    public Guid? LinkFor(ProductIngredientDto ingredient, Guid? currentLink = null)
    {
        if (!ingredient.GlobalIngredientId.HasValue)
        {
            return null;
        }

        var id = ingredient.GlobalIngredientId.Value;
        // Unchanged: the FK held when this was written, so there is nothing to prove — and asking
        // would drop the link the moment the library row is archived.
        if (id == currentLink || _knownIds.Contains(id))
        {
            return id;
        }

        _logger.LogWarning(
            "Global ingredient {GlobalIngredientId} does not exist; saving ingredient {IngredientName} without provenance",
            id, ingredient.Name);
        return null;
    }
}
