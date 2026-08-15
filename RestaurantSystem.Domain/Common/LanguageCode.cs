using System.Collections.Frozen;

namespace RestaurantSystem.Domain.Common;

/// <summary>
/// The one place a language string is validated and canonicalised
/// (EMAIL-LOCALISATION-PLAN §2). Every <c>PreferredLanguage</c> column stores what this
/// returns: a lower-case BCP-47 <em>primary subtag</em> that the product actually has UI
/// and email copy for — <c>fr</c>, never <c>fr-CH</c>, because the resource sets are per
/// language and i18next emits the primary subtag.
/// </summary>
/// <remarks>
/// <see cref="Supported"/> is the product-wide set (the frontend's ten locales,
/// <c>frontend/src/i18n.ts</c>). A tenant may support fewer — that narrower intersection
/// is a configuration concern (<c>LocalizationSettings.SupportedLanguages</c>) and is
/// deliberately not done here, so this helper stays usable by anything that just needs to
/// know whether a string is a language code at all.
/// </remarks>
public static class LanguageCode
{
    /// <summary>Terminal fallback (§1 rank 5). Always a member of <see cref="Supported"/>.</summary>
    public const string Fallback = "en";

    /// <summary>Storage width of every <c>PreferredLanguage</c> column.</summary>
    public const int MaxLength = 10;

    /// <summary>Every language the product ships UI copy for, lower-case, ordered.</summary>
    public static IReadOnlyList<string> Supported { get; } =
        ["ar", "de", "en", "es", "fr", "it", "nl", "ru", "tr", "zh"];

    // Ordinal by construction and O(1): the list above is the published order, this is what
    // Normalize actually tests against, on a path S4 runs per write and S5 per mail.
    private static readonly FrozenSet<string> SupportedLookup =
        Supported.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The canonical code for <paramref name="value"/>, or <c>null</c> when it is blank,
    /// malformed, or a language the product has no copy for. Null means "no preference
    /// recorded" and lets the caller fall through to the next rank of §1 — it is never an
    /// error, so a guest sending a header nobody translated still gets a mail.
    /// </summary>
    /// <remarks>
    /// Strictly ONE tag. An <c>Accept-Language</c> header is a weighted <em>list</em> and is
    /// rejected here rather than half-understood: truncating at the first separator turns
    /// <c>"fr-CH,fr;q=0.9,en"</c> into <c>fr</c> — which looks right — while <c>"fr,en;q=0.9"</c>,
    /// the equally common region-less form, would parse as nothing at all and silently lose the
    /// guest's language. Choosing between the entries of a list is
    /// <c>IEmailLanguageResolver</c>'s job, because only it knows what the tenant supports.
    /// </remarks>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();

        // A list, a quality parameter or embedded whitespace means the caller handed over
        // something that is not a single language tag. Refuse it whole.
        if (candidate.Any(character => character is ',' or ';' || char.IsWhiteSpace(character)))
        {
            return null;
        }

        // "fr-CH", "fr_CH" and "zh-Hans-CN" all reduce to their first subtag.
        var separator = candidate.IndexOfAny(['-', '_']);
        if (separator >= 0)
        {
            candidate = candidate[..separator];
        }

        candidate = candidate.ToLowerInvariant();

        return SupportedLookup.Contains(candidate) ? candidate : null;
    }
}
