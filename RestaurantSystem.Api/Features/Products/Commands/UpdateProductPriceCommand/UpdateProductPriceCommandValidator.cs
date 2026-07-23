using FluentValidation;

namespace RestaurantSystem.Api.Features.Products.Commands.UpdateProductPriceCommand;

public class UpdateProductPriceCommandValidator : AbstractValidator<UpdateProductPriceCommand>
{
    public UpdateProductPriceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Product ID is required");
        // Match UpdateProductCommandValidator: a base price is non-negative.
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).WithMessage("Price must be non-negative");
        // Product.BasePrice is decimal(10,2). Reject over-precision / oversized values with a 400
        // rather than letting them overflow the column into a 500 on SaveChanges.
        RuleFor(x => x.Price)
            .PrecisionScale(10, 2, ignoreTrailingZeros: false)
            .WithMessage("Price must have at most 2 decimal places and be within 99,999,999.99");
    }
}
