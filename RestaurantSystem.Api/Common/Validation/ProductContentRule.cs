using FluentValidation;
using RestaurantSystem.Api.Features.Products.Dtos;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// Shared FluentValidation rule for a <see cref="ProductDescriptionsDto"/> translation map, used by
/// the product and menu-bundle create/update commands (#306).
///
/// FOUR handlers write <c>ProductDescription</c> rows out of this one DTO —
/// <c>CreateProductCommand</c>, <c>UpdateProductCommand</c>, <c>CreateMenuBundleCommand</c>,
/// <c>UpdateMenuBundleCommand</c> — and none of them validated it. The rule lives here rather than
/// being copied into each validator because the copies are exactly what went wrong the last time:
/// the same dead duplicate-language-code check was pasted into all four and had to be removed from
/// each separately (#192, #193).
/// </summary>
public static class ProductContentRule
{
    public const string LanguageKeyRequiredMessage = "A translation's language code is required";
    public const string EntryRequiredMessage = "A translation entry cannot be null";
    public const string NameRequiredMessage = "A translation's name is required";
    public const string DescriptionRequiredMessage = "A translation's description is required";

    /// <summary>
    /// Added by <see cref="ValidProductContent"/> itself when a call site passes
    /// <c>required: true</c>, which only the two CREATE validators do. The asymmetry is deliberate
    /// rather than an oversight:
    ///
    /// <para>The two CREATE handlers dereference <c>command.Content</c> with no coalesce
    /// (<c>command.Content.Select(x => x.Key)</c>), so an omitted <c>content</c> threw
    /// ArgumentNullException and leaked its internal message to the client — measured as
    /// 400 <c>"Value cannot be null. (Parameter 'source')"</c>. Requiring the map keeps that request
    /// REJECTED exactly as it already was and only makes the refusal stateable. Whether a product may
    /// be created with no translations at all is a product decision, not a defect fix.</para>
    ///
    /// <para>The two UPDATE handlers coalesce instead (<c>?? new ProductDescriptionsDto()</c>), where
    /// an absent map legitimately means "no translation changes" — the #190 contract, pinned by
    /// <c>ProductUpdateContentTests.OmittedContentKey_LeavesExistingDescriptionsUntouched</c>. Adding
    /// NotNull there would 400 every save that does not touch translations, which is the admin
    /// editor's ordinary case.</para>
    /// </summary>
    public const string ContentRequiredMessage = "Translations are required";

    /// <summary>
    /// Every entry must carry a non-blank language code, a non-null value, and non-null
    /// <c>Name</c>/<c>Description</c>. A <c>null</c> map passes — "no translation changes" is a
    /// legitimate payload on the update paths, and the create paths gate on their own rule.
    /// </summary>
    /// <remarks>
    /// All four shapes were measured through <c>PUT /api/Products/{id}</c> before this rule existed:
    ///
    ///   <c>"en": { "name": "x" }</c>        (no description)  → HTTP 500
    ///   <c>"en": { "description": "x" }</c> (no name)         → HTTP 500
    ///   <c>"en": null</c>                                     → HTTP 500
    ///   <c>"": {...}</c> / <c>"   ": {...}</c>                → HTTP 200, junk row persisted
    ///
    /// <c>ProductDescriptionDto</c> declares <c>Name</c> and <c>Description</c> as <c>null!</c>, so
    /// an omitted key binds to null however non-nullable it looks; <c>ProductDescription.Description</c>
    /// is <c>required</c> against a non-nullable column, so the whole <c>SaveChangesAsync</c> throws.
    /// The failure is atomic — the seeded rows were verified unchanged after each 500 — so this is a
    /// bad-RESPONSE defect, not data loss, and it violates CLAUDE.md §5.4 (user-facing errors are
    /// BadRequestException, never a generic 500).
    ///
    /// Blank keys are the opposite failure: accepted silently, persisting a <c>Lang = ''</c> row that
    /// no locale will ever match.
    ///
    /// EMPTY Name/Description are deliberately ALLOWED, only null is refused. The admin UI sends
    /// <c>description: data.description || ''</c> (productFormUtils.ts), so a product with no
    /// description text posts an empty string on every save — rejecting it would turn a routine,
    /// currently-working edit into a 400, which is a bigger defect than the one being fixed. The
    /// language KEY is held to the stricter non-blank test because no client sends a blank one and
    /// it is the junk-row case this rule exists to stop.
    /// </remarks>
    /// <param name="required">
    /// <c>true</c> on the CREATE paths, whose handlers dereference the map unguarded — see
    /// <see cref="ContentRequiredMessage"/> for why the UPDATE paths must leave this <c>false</c>.
    /// Carried as a parameter rather than a second <c>RuleFor</c> at the call site so the whole
    /// contract for this field is one line and one decision per validator. It has NO default: the
    /// permissive value is the one you would get by forgetting, and an earlier draft did exactly
    /// that — it put the rule on the shared bundle base AND passed <c>required: true</c> in the
    /// derived create validator, registering it twice (AbstractValidator appends, it does not
    /// dedupe) so every malformed bundle answered with each message duplicated. Each of the four
    /// validators now states this once, explicitly.
    /// </param>
    public static IRuleBuilderOptionsConditions<T, ProductDescriptionsDto?> ValidProductContent<T>(
        this IRuleBuilder<T, ProductDescriptionsDto?> ruleBuilder, bool required) =>
        ruleBuilder.Custom((content, context) =>
        {
            if (content is null)
            {
                if (required)
                {
                    context.AddFailure(ContentRequiredMessage);
                }

                return;
            }

            foreach (var (languageCode, entry) in content)
            {
                if (string.IsNullOrWhiteSpace(languageCode))
                {
                    context.AddFailure(LanguageKeyRequiredMessage);
                    continue;
                }

                // Reported against the language code so a multi-language save says WHICH translation
                // is malformed. FluentValidation's own indexer naming cannot do this: the map is a
                // Dictionary, so a positional `Content[0]` would name a slot the client never sent.
                if (entry is null)
                {
                    context.AddFailure($"{EntryRequiredMessage} ('{languageCode}')");
                    continue;
                }

                if (entry.Name is null)
                {
                    context.AddFailure($"{NameRequiredMessage} ('{languageCode}')");
                }

                if (entry.Description is null)
                {
                    context.AddFailure($"{DescriptionRequiredMessage} ('{languageCode}')");
                }
            }
        });
}
