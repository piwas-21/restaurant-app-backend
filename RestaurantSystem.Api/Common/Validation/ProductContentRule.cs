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
    /// Every entry must carry a non-blank language code, a non-null value, a NON-BLANK <c>Name</c>
    /// and a non-null <c>Description</c>. A <c>null</c> map passes — "no translation changes" is a
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
    /// An EMPTY <c>Description</c> is deliberately ALLOWED, and only null is refused. The admin UI
    /// sends <c>description: data.description || ''</c> (productFormUtils.ts), so a product with no
    /// description text posts an empty string on every save — rejecting it would turn a routine,
    /// currently-working edit into a 400, which is a bigger defect than the one being fixed.
    ///
    /// <c>Name</c> IS held to the stricter non-blank test, since #325, and so is the language KEY.
    /// An earlier version of this rule allowed an empty or whitespace-only name on the stated ground
    /// that — unlike the nested maps — this one has no silent discard, because it stores what it is
    /// given. THAT GROUND WAS WRONG, and an adversarial review measured the sequence rather than
    /// reasoning about it:
    ///
    ///   1. <c>PUT /api/Products/{id}</c> with <c>"en": { "name": "   ", "description": "Une pizza" }</c>
    ///      answered 200 and persisted the row.
    ///   2. The admin editor's payload builder drops that row from the NEXT save —
    ///      <c>productFormUtils.ts</c> filters on <c>e?.name?.trim()</c>, and <c>"   ".trim()</c> is falsy.
    ///   3. <c>UpdateProductCommandHandler</c> does <c>if (contentMap.Any()) RemoveRange(...)</c> — a
    ///      FULL REPLACE — and re-adds only what was sent.
    ///
    /// So the translation, DESCRIPTION TEXT INCLUDED, is silently deleted by a later save that never
    /// mentioned it. Same class as the defect NestedContentRule refuses; the mechanism is the client
    /// filter plus the full replace rather than a handler guard, which is why #323's fix did not
    /// touch it. Of the three candidates #325 lists this is the one that closes the window, because
    /// the window IS a client other than the editor: a client-side <c>.trim().min(1)</c> cannot
    /// constrain a caller that is not the client.
    ///
    /// NO RELEASE ORDER, unlike NestedContentRule — and that is measured, not assumed. The editor
    /// builds this map in two places: the EDIT path already filters blank names off the wire, and
    /// the CREATE path seeds <c>content[currentLanguage].name</c> from the product's own name, whose
    /// only blank spelling is already refused by <c>RuleFor(x => x.Name).NotEmpty()</c> — asserted by
    /// <c>ProductContentRuleTests.AWhitespaceProductName_IsAlreadyRefused_…</c>, because "NotEmpty
    /// rejects whitespace" is the load-bearing fact and its name does not say so.
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

                // BLANK, not merely null (#325): the same IsNullOrWhiteSpace test NestedContentRule
                // applies to the nested maps. Description below stays a null-only check.
                if (string.IsNullOrWhiteSpace(entry.Name))
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
