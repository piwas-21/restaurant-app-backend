using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.DeleteReservationCommand;

public class DeleteReservationCommandValidator : AbstractValidator<DeleteReservationCommand>
{
    public DeleteReservationCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty().WithMessage("ReservationId is required");
    }
}
