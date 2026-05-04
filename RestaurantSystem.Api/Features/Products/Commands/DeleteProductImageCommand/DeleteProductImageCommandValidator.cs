using FluentValidation;

namespace RestaurantSystem.Api.Features.Products.Commands.DeleteProductImageCommand;

public class DeleteProductImageCommandValidator : AbstractValidator<DeleteProductImageCommand>
{
    public DeleteProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Product ID is required");
        RuleFor(x => x.ImageId).NotEmpty().WithMessage("Image ID is required");
    }
}
