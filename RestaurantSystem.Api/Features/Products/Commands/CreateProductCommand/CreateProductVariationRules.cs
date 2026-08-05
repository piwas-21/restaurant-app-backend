using FluentValidation;

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
/// Note for #316 (the nested variation/ingredient content maps, still unvalidated):
/// <c>UpdateProductCommandValidator</c> has NO variation rules at all, so it is not a caller yet.
/// Making it one is where the variation-content rule belongs, and having this here is what keeps that
/// a small change rather than a copy — the pasted-into-four-places pattern is exactly what #192/#193
/// had to unpick.
/// </remarks>
public static class CreateProductVariationRules
{
    public static void Apply(InlineValidator<CreateProductVariationDto> variation)
    {
        ArgumentNullException.ThrowIfNull(variation);

        variation.RuleFor(v => v.Name)
            .NotEmpty().WithMessage("Variation name is required")
            .MaximumLength(50).WithMessage("Variation name cannot exceed 50 characters");

        variation.RuleFor(v => v.Description)
            .MaximumLength(200).WithMessage("Variation description cannot exceed 200 characters");

        variation.RuleFor(v => v.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Variation display order cannot be negative");
    }
}
