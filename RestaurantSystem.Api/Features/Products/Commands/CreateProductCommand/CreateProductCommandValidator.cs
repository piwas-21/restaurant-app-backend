using FluentValidation;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required")
            .MaximumLength(100).WithMessage("Product name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");

        RuleFor(x => x.BasePrice)
            .GreaterThan(0).WithMessage("Base price must be greater than 0");

        RuleFor(x => x.PreparationTimeMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Preparation time cannot be negative");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid product type")
            .NotEqual(ProductType.Menu).WithMessage("Use CreateMenuBundle API for creating menus");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Display order cannot be negative");

        RuleFor(x => x.CategoryIds)
            .NotEmpty().WithMessage("At least one category must be selected")
            .Must(x => x.Distinct().Count() == x.Count).WithMessage("Duplicate categories are not allowed");

        RuleFor(x => x.PrimaryCategoryId)
            .Must((command, primaryCategoryId) =>
                !primaryCategoryId.HasValue || command.CategoryIds.Contains(primaryCategoryId.Value))
            .WithMessage("Primary category must be one of the selected categories");

        RuleForEach(x => x.Variations).ChildRules(variation =>
        {
            variation.RuleFor(v => v.Name)
                .NotEmpty().WithMessage("Variation name is required")
                .MaximumLength(50).WithMessage("Variation name cannot exceed 50 characters");

            variation.RuleFor(v => v.Description)
                .MaximumLength(200).WithMessage("Variation description cannot exceed 200 characters");

            variation.RuleFor(v => v.DisplayOrder)
                .GreaterThanOrEqualTo(0).WithMessage("Variation display order cannot be negative");
        });

        RuleFor(x => x.SuggestedSideItemIds)
            .Must(x => x == null || x.Distinct().Count() == x.Count)
            .WithMessage("Duplicate side items are not allowed");
    }

    private bool BeAValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        return Uri.TryCreate(url, UriKind.Absolute, out var result)
            && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
    }
}
