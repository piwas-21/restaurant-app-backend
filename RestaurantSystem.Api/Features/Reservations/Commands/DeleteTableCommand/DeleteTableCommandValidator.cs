using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.DeleteTableCommand;

public class DeleteTableCommandValidator : AbstractValidator<DeleteTableCommand>
{
    public DeleteTableCommandValidator()
    {
        RuleFor(x => x.TableId).NotEmpty().WithMessage("TableId is required");
    }
}
