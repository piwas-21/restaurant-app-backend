using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.UpdateMyReservationCommand;

public class UpdateMyReservationCommandValidator : AbstractValidator<UpdateMyReservationCommand>
{
    public UpdateMyReservationCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty().WithMessage("Reservation ID is required");
        RuleFor(x => x.ReservationData).NotNull().WithMessage("Reservation data is required");

        // Max 100 matches UpdateMyReservationDto's [MaxLength(100)] — DataAnnotations bind first,
        // so 100 is the effective limit; keep both in sync.
        RuleFor(x => x.ReservationData.CustomerName)
            .NotEmpty().WithMessage("Customer name is required")
            .MaximumLength(100)
            .When(x => x.ReservationData != null);

        RuleFor(x => x.ReservationData.CustomerEmail)
            .NotEmpty().EmailAddress().WithMessage("Valid email is required")
            .When(x => x.ReservationData != null);

        RuleFor(x => x.ReservationData.NumberOfGuests)
            .GreaterThan(0).WithMessage("Number of guests must be at least 1")
            .When(x => x.ReservationData != null);

        RuleFor(x => x.ReservationData.EndTime)
            .GreaterThan(x => x.ReservationData.StartTime)
            .WithMessage("End time must be after start time")
            .When(x => x.ReservationData != null);

        // A CALENDAR DAY, never an instant. Refusing a non-midnight value is deliberate: a client
        // that sends its own local midnight with an offset ("2030-05-17T00:00:00+02:00") parses to
        // the previous day on the server, and a loud 400 beats a booking silently moved a day back.
        RuleFor(x => x.ReservationData.ReservationDate)
            .Must(d => d.TimeOfDay == TimeSpan.Zero)
            .WithMessage("Reservation date must be a calendar day at midnight UTC, e.g. 2030-05-17T00:00:00Z")
            .When(x => x.ReservationData != null);
    }
}
