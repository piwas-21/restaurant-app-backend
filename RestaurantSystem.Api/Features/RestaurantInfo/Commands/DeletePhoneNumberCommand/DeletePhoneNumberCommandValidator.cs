using FluentValidation;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeletePhoneNumberCommand;

public class DeletePhoneNumberCommandValidator : AbstractValidator<DeletePhoneNumberCommand>
{
    public DeletePhoneNumberCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Phone number ID is required");
    }
}
