namespace RestaurantSystem.Api.Common.Utilities;

/// <summary>
/// Where an APPENDED catalog row goes: one past the highest <c>DisplayOrder</c> in use, never the
/// row count (plan S8).
/// </summary>
/// <remarks>
/// <para>
/// <b>The column is not contiguous, and that is measured rather than feared.</b> Nothing wrote
/// <c>DisplayOrder</c> after row creation until frontend #593, so live ingredient and variation rows
/// hold GAPS and DUPLICATES — which is why <c>useVariationReorder</c> re-stamps the whole array on a
/// move instead of shifting one entry. A product whose two rows sit at 2 and 7 has a COUNT of 2, so
/// appending at the count lands on top of the row already at 2, and <c>DisplayOrder</c> is what every
/// consumer sorts by: the admin's "add to the end" would silently insert into the middle, and two
/// rows would then share a position with the tie broken by whatever the database felt like.
/// </para>
/// <para>
/// Taking the maximum is collision-free whatever the column looks like and keeps the new row at the
/// END, which is where the admin asked for it. It is the SAME rule as the front end's
/// <c>nextVariationDisplayOrder</c> (<c>globalVariationLibrary.ts</c>), deliberately: the picker's
/// append and the bulk attach's append must not disagree about where a row lands.
/// </para>
/// <para>
/// It does NOT renumber. Append-then-renumber does not commute with a hand-arranged recipe — a bulk
/// write that tidied the column would reorder forty products nobody asked to reorder — so the gaps
/// survive and a reorder repairs them.
/// </para>
/// </remarks>
public static class CatalogDisplayOrder
{
    /// <summary>
    /// One past the highest value in <paramref name="displayOrdersInUse"/>, or 0 when there is none.
    /// </summary>
    public static int NextAfter(IEnumerable<int> displayOrdersInUse)
    {
        ArgumentNullException.ThrowIfNull(displayOrdersInUse);

        var highest = int.MinValue;
        foreach (var displayOrder in displayOrdersInUse)
        {
            if (displayOrder > highest)
            {
                highest = displayOrder;
            }
        }

        return highest == int.MinValue ? 0 : highest + 1;
    }
}
