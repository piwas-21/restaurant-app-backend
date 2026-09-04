using FluentValidation;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// Shared FluentValidation rule for an allergen label list.
/// </summary>
public static class AllergenListRule
{
    /// <summary>The admin UI offers sixteen chips; the cap is generous headroom over that.</summary>
    private const int MaxLabels = 32;

    /// <summary>Longest token in the EU-14 vocabulary plus the dietary claims is well under this.</summary>
    private const int MaxLabelLength = 40;

    /// <summary>
    /// A list must be <c>null</c>, empty, or a bounded set of short, distinct, non-blank tokens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The admin editor's sixteen chips were the ONLY thing bounding this, and the endpoint that
    /// writes it carries <c>[ApiScope(MenuWrite)]</c> — so a machine token reaches it with no UI in
    /// the way. Unvalidated, that is an unbounded write into a <c>jsonb</c> column.
    /// </para>
    /// <para>
    /// A typo matters beyond size, and is the reason blanks and duplicates are refused rather than
    /// tolerated: the guest filter buckets a token by looking it up, and drops one it does not
    /// recognise. So an unrecognised label is STORED, RENDERED as a chip on the card, and produces
    /// no "No …" chip — the guest sees a warning they cannot filter on. This rule cannot catch a
    /// plausible-looking typo, but it catches the shapes that are never meaningful.
    /// </para>
    /// </remarks>
    public static IRuleBuilderOptions<T, List<string>?> ValidAllergenList<T>(
        this IRuleBuilder<T, List<string>?> ruleBuilder) =>
        ruleBuilder
            .Must(list => list is null || list.Count <= MaxLabels)
            .WithMessage($"No more than {MaxLabels} allergen labels")
            .Must(list => list is null || list.All(l => !string.IsNullOrWhiteSpace(l)))
            .WithMessage("An allergen label cannot be blank")
            .Must(list => list is null || list.All(l => l.Length <= MaxLabelLength))
            .WithMessage($"An allergen label cannot exceed {MaxLabelLength} characters")
            .Must(list => list is null
                || list.Select(l => l.Trim().ToLowerInvariant()).Distinct().Count() == list.Count)
            .WithMessage("The same allergen cannot be listed twice");
}
