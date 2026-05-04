using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ConfirmReservationCommand;

public class ConfirmReservationCommandValidator : AbstractValidator<ConfirmReservationCommand>
{
    public ConfirmReservationCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty().WithMessage("ReservationId is required");
    }
}
