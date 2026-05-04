using FluentValidation;

namespace RestaurantSystem.Api.Features.Addresses.Commands.UpdateAddressCommand;

public class UpdateAddressCommandValidator : AbstractValidator<UpdateAddressCommand>
{
    public UpdateAddressCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Address ID is required");
        RuleFor(x => x.Label).NotEmpty().WithMessage("Label is required").MaximumLength(100);
        RuleFor(x => x.AddressLine1).NotEmpty().WithMessage("Address line 1 is required").MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().WithMessage("City is required").MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().WithMessage("Postal code is required").MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().WithMessage("Country is required").MaximumLength(100);
    }
}
