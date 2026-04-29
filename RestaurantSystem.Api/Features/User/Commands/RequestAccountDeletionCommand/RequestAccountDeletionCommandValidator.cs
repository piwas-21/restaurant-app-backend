using FluentValidation;

namespace RestaurantSystem.Api.Features.User.Commands.RequestAccountDeletionCommand;

public class RequestAccountDeletionCommandValidator : AbstractValidator<RequestAccountDeletionCommand>
{
    public RequestAccountDeletionCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");
    }
}
