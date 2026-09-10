using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Rewrites the DISPLAY names of a loaded order graph into a requested language, in place, before
/// <see cref="OrderMappingService"/> projects it (printer feed only — 2026-09-10 partner request:
/// the print-language switch used to translate the ticket's labels but never the order details,
/// because names are frozen single strings at checkout).
/// <para>
/// Sources, in order: the live catalog's per-language description
/// (<c>Product.Descriptions</c>, <c>ProductIngredient.Descriptions</c>,
/// <c>ProductVariation.Descriptions</c> — the same translations the guest app shows), then the
/// frozen checkout name. Resolving <see cref="OrderItemIngredient.IngredientId"/> against the live
/// recipe is a deliberate carve-out of S1's "readers must never resolve it": the id is used ONLY
/// to find a translated spelling, never to re-derive removals, quantities or the frozen fallback —
/// an orphaned or untranslated row keeps exactly the name checkout froze.
/// </para>
/// <para>
/// Mutating the graph is safe because the only caller polls with AsNoTracking: the rewrite lives
/// and dies with the response. Nothing is persisted.
/// </para>
/// </summary>
public class OrderDisplayTranslator : IOrderDisplayTranslator
{
    /// <summary>
    /// Languages a PC857 receipt can actually render (ADR-002) — the printer-app's own
    /// <c>PrintLanguagePolicy.Supported</i> list. Translations exist for more locales, but
    /// Arabic/Russian/Chinese names would print as garbage on the receipt codepage, so "auto"
    /// resolves only within this set and anything else keeps the frozen (Latin) names.
    /// </summary>
    public static readonly IReadOnlyList<string> PrintSafeLanguages = ["en", "de", "fr", "it", "es", "nl", "tr"];

    public void Apply(IEnumerable<Order> orders, string? language)
    {
        foreach (var order in orders)
        {
            var lang = ResolveFor(order, language);
            if (lang is null)
            {
                continue;
            }

            foreach (var item in order.Items)
            {
                TranslateItem(item, lang);
            }
        }
    }

    /// <summary>
    /// A fixed language applies to every order; "auto" follows each order's own preferred language
    /// (frozen at checkout) when it is print-safe. Unknown codes mean "no translation requested" —
    /// today's behaviour, not an error.
    /// </summary>
    private static string? ResolveFor(Order order, string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var candidate = language.Trim().ToLowerInvariant();
        if (candidate != "auto")
        {
            return PrintSafeLanguages.Contains(candidate) ? candidate : null;
        }

        var preferred = order.PreferredLanguage?.Trim().ToLowerInvariant();
        return preferred is not null && PrintSafeLanguages.Contains(preferred) ? preferred : null;
    }

    private static void TranslateItem(OrderItem item, string lang)
    {
        var productName = TranslateProduct(item.Product, lang);
        if (productName is not null)
        {
            item.ProductName = productName;
        }

        var variation = item.ProductVariation?.Descriptions?
            .FirstOrDefault(d => d.LanguageCode == lang && !string.IsNullOrWhiteSpace(d.Name))?.Name;
        if (!string.IsNullOrWhiteSpace(variation))
        {
            item.VariationName = variation;
        }

        var liveIngredients = item.Product?.DetailedIngredients;
        if (liveIngredients is not null)
        {
            var byId = liveIngredients.Where(i => i.Descriptions is { Count: > 0 }).ToDictionary(i => i.Id);
            foreach (var snapshot in item.IngredientSnapshots)
            {
                if (byId.TryGetValue(snapshot.IngredientId, out var live))
                {
                    var translated = NameFor(live.Descriptions, lang);
                    if (translated is not null)
                    {
                        snapshot.IngredientName = translated;
                    }
                }
            }
        }
    }

    /// <summary>The product display name in <paramref name="lang"/>, or null to keep the frozen name.</summary>
    private static string? TranslateProduct(Product? product, string lang) =>
        product?.Descriptions?.FirstOrDefault(d =>
            d.Lang == lang && !string.IsNullOrWhiteSpace(d.Name))?.Name;

    private static string? NameFor(ICollection<ProductIngredientDescription>? descriptions, string lang) =>
        descriptions?.FirstOrDefault(d => d.LanguageCode == lang && !string.IsNullOrWhiteSpace(d.Name))?.Name;
}
