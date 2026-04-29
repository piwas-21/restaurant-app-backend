using FluentValidation;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInfoCommand;

public class UpdateRestaurantInfoCommandValidator : AbstractValidator<UpdateRestaurantInfoCommand>
{
    public UpdateRestaurantInfoCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters");

        RuleFor(x => x.AddressLine1)
            .NotEmpty().WithMessage("Address line 1 is required")
            .MaximumLength(300).WithMessage("Address line 1 cannot exceed 300 characters");

        RuleFor(x => x.AddressLine2)
            .MaximumLength(300).WithMessage("Address line 2 cannot exceed 300 characters")
            .When(x => x.AddressLine2 != null);

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required")
            .MaximumLength(100).WithMessage("City cannot exceed 100 characters");

        RuleFor(x => x.PostalCode)
            .NotEmpty().WithMessage("Postal code is required")
            .MaximumLength(20).WithMessage("Postal code cannot exceed 20 characters");

        RuleFor(x => x.Country)
            .NotEmpty().WithMessage("Country is required")
            .MaximumLength(100).WithMessage("Country cannot exceed 100 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address");

        RuleFor(x => x.Website)
            .MaximumLength(500).WithMessage("Website cannot exceed 500 characters")
            .When(x => x.Website != null);
    }
}
