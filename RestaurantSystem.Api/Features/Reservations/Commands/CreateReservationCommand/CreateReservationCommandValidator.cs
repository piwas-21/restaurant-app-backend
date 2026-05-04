using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateReservationCommand;

public class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
{
    public CreateReservationCommandValidator()
    {
        RuleFor(x => x.ReservationData).NotNull().WithMessage("Reservation data is required");
        RuleFor(x => x.ReservationData.CustomerName).NotEmpty().WithMessage("Customer name is required").MaximumLength(200).When(x => x.ReservationData != null);
        RuleFor(x => x.ReservationData.CustomerEmail).NotEmpty().EmailAddress().WithMessage("Valid email is required").When(x => x.ReservationData != null);
        RuleFor(x => x.ReservationData.TableId).NotEmpty().WithMessage("Table is required").When(x => x.ReservationData != null);
        RuleFor(x => x.ReservationData.NumberOfGuests).GreaterThan(0).WithMessage("Number of guests must be at least 1").When(x => x.ReservationData != null);
        RuleFor(x => x.ReservationData.EndTime).GreaterThan(x => x.ReservationData.StartTime).WithMessage("End time must be after start time").When(x => x.ReservationData != null);
    }
}
