using FluentValidation;

namespace RestaurantSystem.Api.Features.Addresses.Commands.SetDefaultAddressCommand;

public class SetDefaultAddressCommandValidator : AbstractValidator<SetDefaultAddressCommand>
{
    public SetDefaultAddressCommandValidator()
    {
        RuleFor(x => x.AddressId).NotEmpty().WithMessage("Address ID is required");
    }
}
