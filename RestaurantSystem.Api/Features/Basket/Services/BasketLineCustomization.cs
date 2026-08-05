using System.Text.Json;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// The one rule for "is this the same line?" (#313) — the customization half of it, alongside the
/// identity half (product + variation) that every caller already applies.
///
/// TWO CALLERS, AND THEY USED TO DISAGREE. <c>BasketService.AddItemToBasketAsync</c> ran an identity
/// query and then filtered it with a private <c>IsSameCustomization</c>, deliberately, so two
/// differently-customised lines of the same product stay apart (#155). <c>AnonymousBasketMerger</c>
/// ran the same identity query and stopped there — so logging in merged lines the add path had spent
/// effort keeping separate. Measured on #313: a customised guest line and a plain account line
/// collapsed to one row at 25.98 with the side item HARD-DELETED, and the mirror case charged 31.96,
/// billing an extra on a unit the guest added plain. Same 28.97 built either way.
///
/// SHARING IT MEANT NORMALISING, NOT MOVING. The add path compares a stored row against an incoming
/// request; the merge compares two stored rows. So each source is reduced to this common form and
/// the comparison happens here, once.
///
/// THE STORED SIDE IS TAKEN VERBATIM AND THE REQUEST SIDE IS FILTERED, which looks asymmetric and is
/// deliberate: <c>BuildRegularItemAsync</c> drops non-positive side quantities before persisting, so
/// filtering the request mirrors what the row WOULD have been built as, while filtering a stored row
/// would silently redefine equality for a row that is already on disk. Two stored rows are therefore
/// compared exactly as they were written.
/// </summary>
public sealed class BasketLineCustomization
{
    private readonly string _instructions;
    private readonly List<Guid> _selected;
    private readonly List<Guid> _added;
    private readonly List<(Guid Id, int Quantity)> _sides;
    private readonly List<(Guid Id, int Quantity)> _quantities;
    private readonly List<string> _composition;

    private BasketLineCustomization(
        string? instructions,
        List<Guid>? selected,
        List<Guid>? added,
        List<(Guid, int)> sides,
        Dictionary<Guid, int>? quantities,
        List<string> composition)
    {
        _instructions = instructions ?? "";
        _selected = Sorted(selected);
        _added = Sorted(added);
        _sides = sides.OrderBy(s => s.Item1).ToList();
        _composition = composition;

        // Every ingredient quantity that is a CHOICE, from two sources that a single projection
        // cannot cover:
        //
        //   * a SELECTED ingredient contributes its effective quantity (map value if > 0, else 1), so
        //     a client that omits the default still matches a row that wrote it explicitly;
        //   * an ingredient that is NOT selected but carries a non-zero quantity contributes that
        //     quantity, because a row can hold quantities with no selection at all —
        //     LineCustomizationBuilder persists an explicit client map BEFORE its selection gate, so
        //     `{ ProductId, IngredientQuantities }` with no SelectedIngredients is a real, reachable
        //     row. Projecting only through the selection dropped that column from the comparison
        //     entirely, and a double-onion line then read as identical to a plain one.
        //
        // Backfilled 0-entries are excluded by construction: an unselected ingredient at 0 is what
        // #304's "NO Cheese" derivation is made of, not a choice about what the line contains.
        var selectedSet = _selected.ToHashSet();
        _quantities = _selected
            .Select(id => (id, Effective(quantities, id)))
            .Concat((quantities ?? new Dictionary<Guid, int>())
                .Where(kv => !selectedSet.Contains(kv.Key) && kv.Value != 0)
                .Select(kv => (kv.Key, kv.Value)))
            .OrderBy(q => q.Item1)
            .ToList();
    }

    /// <summary>
    /// The customization of a STORED row, or <c>null</c> when one of its JSON columns cannot be read.
    /// </summary>
    /// <remarks>
    /// Null is an undecidable answer, not an empty one, and <see cref="AreSame"/> never treats it as
    /// equal to anything — including another null. Answering "same" for a row nobody can parse would
    /// merge a real choice into it: on the add path that discards what this customer just asked for
    /// and charges them for the stored line instead; on the merge path it would delete the guest's
    /// row outright. Answering "not same" costs one duplicate line and loses nothing (#188).
    ///
    /// ONE DELIBERATE DIVERGENCE from the private rule this replaced: the old
    /// <c>SameSelectedQuantities</c> returned true on an empty selection BEFORE parsing the quantities
    /// column, so a row with no selected ingredients and a corrupt column still deduped. This parses
    /// unconditionally, so that row now yields a second line instead. No writer in the codebase can
    /// produce such a column — <c>LineCustomizationBuilder</c> always serialises — but #188 exists
    /// because unparseable columns turn up anyway, and the conservative answer is the one this class
    /// gives everywhere else. Pinned by <c>AnUnreadableColumn_WithNothingSelected_StillDoesNotMatch</c>.
    /// </remarks>
    /// <param name="children">
    /// The row's child rows AS LOADED BY THE CALLER — a bundle's chosen options, empty for a regular
    /// line. Load-bearing and silent if wrong, exactly as in <see cref="BasketLineTotal"/>: a bundle
    /// PARENT stores none of its own composition (<c>BuildMenuItemAsync</c> writes only instructions
    /// on it; every option lives on a child), so a caller that passes an empty list reads every bundle
    /// of one menu product as identical. Measured before this was folded in: a guest holding
    /// Combo+Cola and an account holding Combo+Sprite merged into one line, the guest was charged
    /// 26.00 for the 22.00 they built, the kitchen got two Sprites and no Cola, and the guest's parent
    /// and child rows were left behind on the soft-deleted basket — invisible to every read path.
    /// </param>
    public static BasketLineCustomization? FromRow(
        BasketItem row, IReadOnlyCollection<BasketItem> children, Action<Exception, string> onUnreadable)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(onUnreadable);

        if (!TryRead<List<SelectedSideItemDto>>(row.SelectedSideItemsJson, "side items", onUnreadable, out var sides))
        {
            return null;
        }

        if (!TryRead<Dictionary<Guid, int>>(row.IngredientQuantitiesJson, "ingredient quantities", onUnreadable, out var quantities))
        {
            return null;
        }

        // OfType drops null elements from corrupt or hand-edited JSON without a nullable warning, so
        // the projection below cannot NRE. Stored side quantities are NOT filtered — see the class
        // remarks for why the two sources normalise differently.
        var storedSides = (sides ?? new List<SelectedSideItemDto>())
            .OfType<SelectedSideItemDto>()
            .Select(s => (s.Id, s.Quantity))
            .ToList();

        var composition = Composition(row, children, onUnreadable);
        if (composition is null)
        {
            return null;
        }

        return new BasketLineCustomization(
            row.SpecialInstructions, row.SelectedIngredients, row.AddedIngredients,
            storedSides, quantities, composition);
    }

    /// <summary>
    /// A bundle's chosen options, normalised PER UNIT and order-insensitively. Empty for a regular
    /// line; <c>null</c> when the row cannot be reasoned about, which never matches.
    /// </summary>
    /// <remarks>
    /// Per unit, not as stored: a child's <c>Quantity</c> is LINE-ABSOLUTE
    /// (<c>item.Quantity * option.Quantity</c>, kept so by <see cref="BundleChildQuantityScaler"/>),
    /// so two identical builds at different line quantities would otherwise read as different
    /// compositions — and #305's merge case, where the same bundle sits in both baskets, has to keep
    /// merging. A count that does not divide by the parent's quantity is undecidable rather than
    /// rounded, mirroring the scaler's own refusal to invent a number.
    /// </remarks>
    private static List<string>? Composition(
        BasketItem parent, IReadOnlyCollection<BasketItem> children, Action<Exception, string> onUnreadable)
    {
        if (children.Count == 0)
        {
            return new List<string>();
        }

        if (parent.Quantity <= 0)
        {
            return null;
        }

        var entries = new List<string>(children.Count);
        foreach (var child in children)
        {
            if (child.Quantity % parent.Quantity != 0)
            {
                return null;
            }

            // Baskets are one level deep (see BasketLineChannelScan), so a child's own children are
            // not expected; passing none keeps the recursion terminating either way.
            var own = FromRow(child, Array.Empty<BasketItem>(), onUnreadable);
            if (own is null)
            {
                return null;
            }

            entries.Add(FormattableString.Invariant(
                $"{child.ProductId}|{child.ProductVariationId}|{child.Quantity / parent.Quantity}|{own.Key()}"));
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    /// <summary>This line's own customization as one canonical string, so a bundle child can take part
    /// in its parent's composition without a second comparison rule.</summary>
    private string Key() => string.Join('\u001f', [
        _instructions,
        string.Join(',', _selected),
        string.Join(',', _added),
        string.Join(',', _sides.Select(s => FormattableString.Invariant($"{s.Id}:{s.Quantity}"))),
        string.Join(',', _quantities.Select(q => FormattableString.Invariant($"{q.Id}:{q.Quantity}"))),
        string.Join('~', _composition),
    ]);

    /// <summary>The customization an incoming add-to-basket request is asking for. Cannot fail — it
    /// carries objects rather than JSON.</summary>
    public static BasketLineCustomization FromRequest(AddToBasketDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Mirror BuildRegularItemAsync: only positive-quantity sides would be persisted, so only
        // those take part in deciding whether this request IS the stored line.
        var requestedSides = (request.SelectedSideItems ?? new List<SelectedSideItemDto>())
            .Where(s => s.Quantity > 0)
            .Select(s => (s.Id, s.Quantity))
            .ToList();

        // No composition: a request that builds a BUNDLE returns from BasketService's Menu branch
        // before dedup is ever reached, so an incoming request is always a regular line. That also
        // means a stored bundle parent can no longer match one — which is the right answer for the
        // retyped-product case #308 documents, where a stale bundle parent DOES fall into dedup.
        return new BasketLineCustomization(
            request.SpecialInstructions, request.SelectedIngredients, request.AddedIngredients,
            requestedSides, request.IngredientQuantities, new List<string>());
    }

    /// <summary>
    /// Whether two lines carry the same customization. A <c>null</c> operand is undecidable and never
    /// matches — see <see cref="FromRow"/>.
    /// </summary>
    public static bool AreSame(BasketLineCustomization? a, BasketLineCustomization? b) =>
        a is not null
        && b is not null
        && a._instructions == b._instructions
        && a._selected.SequenceEqual(b._selected)
        && a._added.SequenceEqual(b._added)
        && a._sides.SequenceEqual(b._sides)
        && a._quantities.SequenceEqual(b._quantities)
        && a._composition.SequenceEqual(b._composition);

    private static bool TryRead<T>(
        string? json, string what, Action<Exception, string> onUnreadable, out T? value)
        where T : class
    {
        value = null;
        if (string.IsNullOrEmpty(json))
        {
            // A missing column is "no stored selection", which is a legitimate, comparable state —
            // distinct from a column that is present and unparseable.
            return true;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json);
            return true;
        }
        catch (JsonException ex)
        {
            onUnreadable(ex, what);
            return false;
        }
    }

    // Sorted, and NOT de-duplicated: SequenceEqual on sorted lists is multiset equality, which is what
    // the add path has always compared (equal counts plus equal sorted sequences).
    private static List<Guid> Sorted(List<Guid>? ids) =>
        (ids ?? new List<Guid>()).OrderBy(x => x).ToList();

    // A selected ingredient's effective quantity is its map value when > 0, else 1 — so a client that
    // omits the default still matches a stored row that wrote it explicitly.
    private static int Effective(Dictionary<Guid, int>? quantities, Guid ingredientId) =>
        quantities != null && quantities.TryGetValue(ingredientId, out var q) && q > 0 ? q : 1;
}
