using FluentValidation;

namespace RestaurantSystem.Api.Features.Products.Commands.UploadProductImageCommand;

public class UploadProductImageCommandValidator : AbstractValidator<UploadProductImageCommand>
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public UploadProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Product ID is required");
        RuleFor(x => x.Image).NotNull().WithMessage("Image file is required");
        RuleFor(x => x.Image.Length)
            .LessThanOrEqualTo(10 * 1024 * 1024).WithMessage("Image must be 10 MB or smaller")
            .When(x => x.Image != null);
        RuleFor(x => x.Image.FileName)
            .Must(name => AllowedExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
            .WithMessage("Only JPG, PNG, and WebP images are allowed")
            .When(x => x.Image != null);
    }
}
