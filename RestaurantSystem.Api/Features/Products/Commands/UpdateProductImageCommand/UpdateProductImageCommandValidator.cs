using FluentValidation;

namespace RestaurantSystem.Api.Features.Products.Commands.UpdateProductImageCommand;

public class UpdateProductImageCommandValidator : AbstractValidator<UpdateProductImageCommand>
{
    public UpdateProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Product ID is required");
        RuleFor(x => x.ImageId).NotEmpty().WithMessage("Image ID is required");
    }
}
