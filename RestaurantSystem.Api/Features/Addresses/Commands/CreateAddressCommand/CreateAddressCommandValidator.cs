using FluentValidation;

namespace RestaurantSystem.Api.Features.Addresses.Commands.CreateAddressCommand;

public class CreateAddressCommandValidator : AbstractValidator<CreateAddressCommand>
{
    public CreateAddressCommandValidator()
    {
        RuleFor(x => x.Label).NotEmpty().WithMessage("Label is required").MaximumLength(100);
        RuleFor(x => x.AddressLine1).NotEmpty().WithMessage("Address line 1 is required").MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().WithMessage("City is required").MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().WithMessage("Postal code is required").MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().WithMessage("Country is required").MaximumLength(100);
    }
}
