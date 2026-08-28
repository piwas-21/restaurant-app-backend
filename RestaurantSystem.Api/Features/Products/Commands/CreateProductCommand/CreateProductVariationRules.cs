using FluentValidation;
using RestaurantSystem.Api.Common.Validation;

namespace RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;

/// <summary>
/// The per-variation field rules for <see cref="CreateProductCommand"/>. A pure extraction from
/// <see cref="CreateProductCommandValidator"/> — no rule added, removed or reworded.
/// </summary>
/// <remarks>
/// Moved out because that validator sat at exactly the CLAUDE.md §4 60-line limit and #306 had to add
/// one rule to it. The alternative was banking the file into <c>scripts/file-length-baseline.txt</c>,
/// which grandfathers it permanently and lets it grow unbounded; this block was the only thing in the
/// file large enough to move that carries no rationale worth disturbing — the
/// <c>PrimaryCategoryId</c> rules do, encoding the #190 contract and ORDER-TYPE-AVAILABILITY-PLAN
/// §3.4.
///
/// The note that used to stand here said <c>UpdateProductCommandValidator</c> had NO variation rules
/// at all, so this class was not yet its caller, and that making it one was where the fix belonged.
/// S4 did exactly that. The rules are now expressed once, over the two record types' shared fields,
/// and applied from BOTH validators — which is what stopped a 50-character bound from being a 400 on
/// <c>POST</c> and a 500 on <c>PUT</c> (backend analysis §9 defect 1).
/// </remarks>
public static class CreateProductVariationRules
{
    public static void Apply(InlineValidator<CreateProductVariationDto> variation) =>
        variation.ApplyVariationFields(v => v.Name, v => v.Description, v => v.DisplayOrder);
}
