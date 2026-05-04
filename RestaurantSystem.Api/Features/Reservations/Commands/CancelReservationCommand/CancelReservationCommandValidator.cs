using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CancelReservationCommand;

public class CancelReservationCommandValidator : AbstractValidator<CancelReservationCommand>
{
    public CancelReservationCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty().WithMessage("ReservationId is required");
    }
}
