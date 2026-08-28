using FluentValidation;

namespace RestaurantSystem.Api.Features.GlobalVariations.Commands.AttachGlobalVariationCommand;

/// <summary>
/// The batch bounds for a bulk variation attach (plan S8).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is deliberately no rule on the price modifier's SIGN.</b> A negative modifier is normal —
/// "Small −1.00" is a discount, not a mistake — and the danger it carries is not a property of the
/// number but of the PRODUCT it lands on: it lowers the cheapest sellable price, which can drop below
/// the product's removable included-in-base value and price a line under zero. That is per product,
/// so it is checked per product by <c>GlobalVariationAttach.Fits</c> and refuses the whole batch by
/// name. A blanket <c>GreaterThanOrEqualTo(0)</c> here would refuse a legitimate discount everywhere
/// AND still miss the products where a small negative modifier is the one that breaks the guard.
/// </para>
/// </remarks>
public class AttachGlobalVariationCommandValidator : AbstractValidator<AttachGlobalVariationCommand>
{
    /// <summary>
    /// A sane ceiling rather than a business rule: the whole batch is loaded with its ingredients
    /// and variations to be validated, and the live catalogue is 77 products.
    /// </summary>
    private const int MaxProductsPerBatch = 500;

    public AttachGlobalVariationCommandValidator()
    {
        RuleFor(command => command.Body.ProductIds)
            .NotEmpty().WithMessage("Select at least one product.")
            .Must(ids => ids.Count <= MaxProductsPerBatch)
            .WithMessage($"A bulk attach covers at most {MaxProductsPerBatch} products at a time.");

        RuleFor(command => command.Body.ProductIds)
            .Must(ids => ids.TrueForAll(id => id != Guid.Empty))
            .WithMessage("A product id is empty.");
    }
}
