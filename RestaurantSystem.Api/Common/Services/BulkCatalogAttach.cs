using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// The part of a bulk attach that is the same whichever library the row came from (plan S8): decide
/// which of the requested products this write will touch, and refuse the whole batch when any target
/// would end up invalid.
/// </summary>
/// <remarks>
/// <para>
/// <b>All-or-nothing on a rule violation, itemised skips otherwise.</b> A product that already
/// carries this row, or an id that resolves to no product, is REPORTED and stepped over: a bulk
/// action that refused forty products because one was already done would be unusable, and neither
/// case is an error. A product the write would leave INVALID is different — nothing is written at
/// all and the response names every offender, because that state is a money defect and the admin has
/// to decide which way out of it. Plan §6 buys protection from "irreversible bulk edit"; refusing
/// BEFORE writing is how that promise is kept.
/// </para>
/// <para>
/// What is NOT shared is what each attach copies and what it must check — the ingredient guard reads
/// the new row's own price, the variation guard reads a modifier that moves the price FLOOR — so
/// those stay in the two <c>*Attach</c> services where they can be read next to their reasoning.
/// </para>
/// </remarks>
internal static class BulkCatalogAttach
{
    /// <summary>
    /// Splits the requested ids into the products this write will touch and the ones it will not,
    /// recording a reason for every skip. Nothing is dropped in silence — a bulk action that reported
    /// "done" while quietly missing four of forty is the one that gets trusted wrongly.
    /// </summary>
    /// <param name="alreadyLinked">
    /// Idempotence, by PROVENANCE rather than by name: attaching twice must not give one product two
    /// copies of one library row, which would also make S3's "used on N items" a lie about N, since
    /// that count is DISTINCT by product.
    /// </param>
    public static List<Product> Triage(
        IReadOnlyCollection<Guid> requested,
        IReadOnlyCollection<Product> products,
        Func<Product, bool> alreadyLinked,
        List<AttachSkippedProductDto> skipped)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(alreadyLinked);
        ArgumentNullException.ThrowIfNull(skipped);

        var found = products.Select(p => p.Id).ToHashSet();
        skipped.AddRange(requested
            .Where(id => !found.Contains(id))
            .Select(id => new AttachSkippedProductDto { ProductId = id, Reason = AttachSkipReasons.NotFound }));

        var targets = new List<Product>();
        foreach (var product in products)
        {
            if (alreadyLinked(product))
            {
                skipped.Add(new AttachSkippedProductDto
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Reason = AttachSkipReasons.AlreadyLinked,
                });
                continue;
            }

            targets.Add(product);
        }

        return targets;
    }

    /// <summary>The names of every target the write would leave invalid, in target order.</summary>
    public static List<string> Refused(IEnumerable<Product> targets, Func<Product, bool> fits)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(fits);

        return targets.Where(product => !fits(product)).Select(product => product.Name).ToList();
    }
}
