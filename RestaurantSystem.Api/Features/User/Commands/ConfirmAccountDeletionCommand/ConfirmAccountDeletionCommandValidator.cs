using FluentValidation;

namespace RestaurantSystem.Api.Features.User.Commands.ConfirmAccountDeletionCommand;

public class ConfirmAccountDeletionCommandValidator : AbstractValidator<ConfirmAccountDeletionCommand>
{
    public ConfirmAccountDeletionCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Confirmation token is required");
    }
}
