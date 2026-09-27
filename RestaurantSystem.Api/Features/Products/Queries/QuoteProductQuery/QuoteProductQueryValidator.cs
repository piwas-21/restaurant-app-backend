using FluentValidation;

namespace RestaurantSystem.Api.Features.Products.Queries.QuoteProductQuery;

public sealed class QuoteProductQueryValidator : AbstractValidator<QuoteProductQuery>
{
    public QuoteProductQueryValidator()
    {
        RuleFor(query => query.ProductId).NotEmpty();
        RuleFor(query => query.Request).NotNull();
        RuleFor(query => query.Request.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Quantity cannot exceed 100");
        RuleFor(query => query.Request.SpecialInstructions)
            .MaximumLength(500).WithMessage("Special instructions cannot exceed 500 characters");
        RuleForEach(query => query.Request.SelectedMenuOptions).ChildRules(option =>
        {
            option.RuleFor(item => item.Quantity)
                .GreaterThan(0).WithMessage("Menu option quantity must be greater than 0")
                .LessThanOrEqualTo(100).WithMessage("Menu option quantity cannot exceed 100");
            option.RuleFor(item => item.SpecialInstructions)
                .MaximumLength(500).WithMessage("Special instructions cannot exceed 500 characters");
        });
        RuleForEach(query => query.Request.SelectedSideItems).ChildRules(sideItem =>
        {
            sideItem.RuleFor(item => item.Quantity)
                .LessThanOrEqualTo(100).WithMessage("Side item quantity cannot exceed 100");
        });
    }
}
