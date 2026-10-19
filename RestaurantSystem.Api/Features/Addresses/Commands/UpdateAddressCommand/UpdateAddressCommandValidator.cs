using FluentValidation;
using RestaurantSystem.Api.Features.Addresses.Validation;

namespace RestaurantSystem.Api.Features.Addresses.Commands.UpdateAddressCommand;

public sealed class UpdateAddressCommandValidator : AddressContentValidator<UpdateAddressCommand>
{
    public UpdateAddressCommandValidator() => RuleFor(value => value.Id).NotEmpty().WithMessage("Address ID is required");
}
