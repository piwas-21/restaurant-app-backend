using FluentValidation;

namespace RestaurantSystem.Api.Features.Addresses.Commands.DeleteAddressCommand;

public class DeleteAddressCommandValidator : AbstractValidator<DeleteAddressCommand>
{
    public DeleteAddressCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Address ID is required");
    }
}
