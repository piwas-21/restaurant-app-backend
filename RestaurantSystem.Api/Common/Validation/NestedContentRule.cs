using System.Linq.Expressions;
using FluentValidation;
using RestaurantSystem.Api.Features.Products.Dtos;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// Shared FluentValidation rule for the NESTED translation maps — a variation's and an ingredient's
/// <c>Content</c> — on the product create and update commands (#316).
///
/// <see cref="ProductContentRule"/> fixed the TOP-LEVEL map. These two were left unvalidated in the
/// same two handlers, and they fail harder, because their columns are configured where the top-level
/// ones are not: <c>ProductDescription</c> has no <c>IEntityTypeConfiguration</c> so its columns are
/// unbounded <c>text</c>, while <c>ProductIngredientDescription.LanguageCode</c> /
/// <c>ProductVariationDescription.LanguageCode</c> are <c>varchar(10)</c> and
/// <c>ProductIngredientDescription.Name</c> is <c>varchar(200)</c>. So these maps add
/// length-overflow 500s the fixed map cannot produce.
///
/// A SIBLING RULE RATHER THAN A REUSE, deliberately. The two content DTOs are shaped alike but are
/// distinct types, and — the part that matters — their <c>Description</c> is genuinely nullable by
/// design, unlike <c>ProductDescriptionDto</c>'s. Pointing <c>ProductContentRule</c> at them would
/// have rejected a null description, which is a legitimate payload here: the nested entities' own
/// <c>Description</c> columns are nullable. Only <c>Name</c> is required.
/// </summary>
public static class NestedContentRule
{
    /// <summary>Matches <c>varchar(10)</c> on both nested description entities' LanguageCode.</summary>
    public const int LanguageCodeMaxLength = 10;

    /// <summary>Matches <c>varchar(200)</c> on ProductIngredientDescription.Name.</summary>
    public const int IngredientNameMaxLength = 200;

    /// <summary>
    /// Matches <c>varchar(100)</c> on ProductVariationDescription.Name. An earlier version of this
    /// file claimed that column was unbounded and passed no bound for it, which left a 101-character
    /// variation name reaching the database as a 500 — the very shape this rule exists to convert
    /// into a 400. The bound was read from
    /// <c>ProductVariationDescriptionConfiguration</c>, not assumed from its sibling.
    /// </summary>
    public const int VariationNameMaxLength = 100;

    /// <summary>
    /// Matches <c>varchar(500)</c> on BOTH nested description entities' Description. Nullable is not
    /// the same as unbounded: these columns accept null, which is why the rule does not require a
    /// description, but they still overflow at 501 characters.
    /// </summary>
    public const int DescriptionMaxLength = 500;

    public const string LanguageKeyRequiredMessage = "A translation's language code is required";
    public const string EntryRequiredMessage = "A translation entry cannot be null";
    public const string NameRequiredMessage = "A translation's name is required";

    public static string LanguageKeyTooLongMessage(string languageCode) =>
        $"Language code '{languageCode}' is longer than {LanguageCodeMaxLength} characters";

    public static string NameTooLongMessage(string languageCode, int max) =>
        $"A translation's name is longer than {max} characters ('{languageCode}')";

    public static string DescriptionTooLongMessage(string languageCode) =>
        $"A translation's description is longer than {DescriptionMaxLength} characters ('{languageCode}')";

    /// <summary>
    /// Every entry must carry a language code that is non-blank and fits its column, a non-null value,
    /// and a non-null <c>Name</c> that fits its column. A <c>null</c> map passes — an absent
    /// <c>content</c> on a nested item legitimately means "no translations", exactly as on the update
    /// path's top-level map.
    /// </summary>
    /// <remarks>
    /// Six shapes were measured through <c>PUT /api/Products/{id}</c> as admin BEFORE this rule, with
    /// #306's top-level fix already in place:
    ///
    ///   ingredient <c>{"en": {"description":"d"}}</c> (null name)  → HTTP 500
    ///   ingredient <c>{"en": null}</c>                             → HTTP 500
    ///   ingredient <c>{"": {"name":"n"}}</c>                       → HTTP 200, junk row persisted
    ///   ingredient <c>{"averyverylonglanguagecode": …}</c>         → HTTP 500 (varchar(10) overflow)
    ///   variation  <c>{"en": null}</c>                             → HTTP 500
    ///   variation  <c>{"": {"name":"n","description":"d"}}</c>     → HTTP 200, junk row persisted
    ///
    /// Two of the six answer 200 AND WRITE A ROW, so coverage has to assert the row is absent rather
    /// than assert the status — a status-only test reads those as success.
    ///
    /// The blank-key case is the same junk-row failure <see cref="ProductContentRule"/> exists to
    /// stop; the oversize-key case is new here and is why the length rules are part of this fix
    /// rather than a follow-up. Fixing only the nulls would leave two of the six 500s standing.
    ///
    /// EMPTY <c>Name</c> is allowed and only null is refused, matching the top-level rule's reasoning:
    /// the admin UI posts empty strings for untouched fields, so rejecting them would turn a routine,
    /// currently-working save into a 400.
    /// </remarks>
    /// <param name="name">Reads the entry's <c>Name</c> — the one required field the two DTOs share
    /// under different types.</param>
    /// <param name="nameMaxLength">The <c>Name</c> column's limit, or <c>null</c> where it is
    /// unbounded. Explicit at every call site rather than defaulted, because the permissive value is
    /// the one you get by forgetting.</param>
    public static IRuleBuilderOptionsConditions<T, Dictionary<string, TContent>?> ValidNestedContent<T, TContent>(
        this IRuleBuilder<T, Dictionary<string, TContent>?> ruleBuilder,
        Func<TContent, string?> name,
        Func<TContent, string?> description,
        int nameMaxLength)
        where TContent : class =>
        ruleBuilder.Custom((content, context) =>
        {
            if (content is null)
            {
                return;
            }

            foreach (var (languageCode, entry) in content)
            {
                if (string.IsNullOrWhiteSpace(languageCode))
                {
                    context.AddFailure(LanguageKeyRequiredMessage);
                    continue;
                }

                if (languageCode.Length > LanguageCodeMaxLength)
                {
                    context.AddFailure(LanguageKeyTooLongMessage(languageCode));
                    continue;
                }

                // Reported against the language code, like the top-level rule: the map is a
                // Dictionary, so a positional index would name a slot the client never sent.
                if (entry is null)
                {
                    context.AddFailure($"{EntryRequiredMessage} ('{languageCode}')");
                    continue;
                }

                var entryName = name(entry);
                if (entryName is null)
                {
                    context.AddFailure($"{NameRequiredMessage} ('{languageCode}')");
                }
                else if (entryName.Length > nameMaxLength)
                {
                    context.AddFailure(NameTooLongMessage(languageCode, nameMaxLength));
                }

                // Nullable, so no null check — but bounded, so a length one. Omitting this left a
                // 501-character description reaching varchar(500) as a 500.
                if (description(entry) is { } text && text.Length > DescriptionMaxLength)
                {
                    context.AddFailure(DescriptionTooLongMessage(languageCode));
                }
            }
        });

    /// <summary>
    /// Registers a nested collection's content rule on <paramref name="validator"/>.
    /// </summary>
    /// <remarks>
    /// Both product validators need the identical pair of registrations. They were 52 and 56 lines
    /// against the §4 limit of 60 — NOT "exactly on it", which is what #315 says of the validators it
    /// names and what an earlier version of this comment repeated without checking — but writing the
    /// pair out twice, with its rationale, still pushed both over and forced a baseline-or-decompose
    /// choice. The lines that did it were the EXPLANATION, not the rules, which IS #315's observation:
    /// so the explanation lives here, at the rule, and each validator states its intent in a line. It is also the copy-paste
    /// <see cref="ProductContentRule"/> warns about: the last duplicated check had to be deleted from
    /// four validators separately.
    ///
    /// Generic over the ITEM type because the create and update commands declare their own
    /// (<c>CreateProductVariationDto</c> / <c>UpdateProductVariationDto</c>); the CONTENT types are
    /// shared, which is what makes one rule serve both commands.
    /// </remarks>
    public static void ValidateNestedContent<T, TItem, TContent>(
        this AbstractValidator<T> validator,
        Expression<Func<T, IEnumerable<TItem>?>> items,
        Expression<Func<TItem, Dictionary<string, TContent>?>> content,
        Func<TContent, string?> name,
        Func<TContent, string?> description,
        int nameMaxLength)
        where TContent : class
    {
        ArgumentNullException.ThrowIfNull(validator);
        validator.RuleForEach(items).ChildRules(item =>
            item.RuleFor(content).ValidNestedContent(name, description, nameMaxLength));
    }
}
