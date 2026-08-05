using FluentValidation;

namespace RestaurantSystem.Api.Features.Basket.Commands.AddToBasketCommand;

public class AddToBasketCommandValidator : AbstractValidator<AddToBasketCommand>
{
    public AddToBasketCommandValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage("Session ID is required");

        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Quantity cannot exceed 100");

        RuleFor(x => x.SpecialInstructions)
            .MaximumLength(500).WithMessage("Special instructions cannot exceed 500 characters");

        // Bundle-child SpecialInstructions are persisted on child BasketItem rows
        // (varchar(500)) since issue #150; without this rule an oversized value
        // would surface as a DbUpdateException (HTTP 500) instead of a clean 400.
        RuleForEach(x => x.SelectedMenuOptions).ChildRules(option =>
        {
            option.RuleFor(o => o.SpecialInstructions)
                .MaximumLength(500).WithMessage("Special instructions cannot exceed 500 characters");

            // The rule above binds the LINE quantity; an option's own had no ceiling anywhere
            // (#308), so 30,000,000 was accepted and overflowed the decimal price column later.
            option.RuleFor(o => o.Quantity)
                .GreaterThan(0).WithMessage("Menu option quantity must be greater than 0")
                .LessThanOrEqualTo(100).WithMessage("Menu option quantity cannot exceed 100");
        });

        // The command's other client-supplied quantity, same hole (#308), measured at 500 on the
        // INSERT. UPPER BOUND ONLY — the asymmetry with the option rule is deliberate and is
        // explained, with what breaks if it is "tidied" into symmetry, in
        // AddToBasketCommandValidatorTests.
        RuleForEach(x => x.SelectedSideItems).ChildRules(sideItem =>
        {
            sideItem.RuleFor(si => si.Quantity)
                .LessThanOrEqualTo(100).WithMessage("Side item quantity cannot exceed 100");
        });
    }
}
