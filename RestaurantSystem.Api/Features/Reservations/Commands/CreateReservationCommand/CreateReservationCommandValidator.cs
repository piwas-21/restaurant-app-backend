using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateReservationCommand;

public class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
{
    public CreateReservationCommandValidator()
    {
        RuleFor(x => x.ReservationData).NotNull().WithMessage("Reservation data is required");
        // Max 100 matches CreateReservationDto's [MaxLength(100)] — DataAnnotations bind
        // first, so 100 is the effective limit; keep both in sync.
        RuleFor(x => x.ReservationData.CustomerName).NotEmpty().WithMessage("Customer name is required").MaximumLength(100).When(x => x.ReservationData != null);
        RuleFor(x => x.ReservationData.CustomerEmail).NotEmpty().EmailAddress().WithMessage("Valid email is required").When(x => x.ReservationData != null);
        RuleFor(x => x.ReservationData.TableId).NotEmpty().WithMessage("Table is required").When(x => x.ReservationData != null);
        RuleFor(x => x.ReservationData.NumberOfGuests).GreaterThan(0).WithMessage("Number of guests must be at least 1").When(x => x.ReservationData != null);
        RuleFor(x => x.ReservationData.EndTime).GreaterThan(x => x.ReservationData.StartTime).WithMessage("End time must be after start time").When(x => x.ReservationData != null);
        // Combined tables (#561): ONE reservation over N tables. The list must not repeat itself
        // and must not repeat the primary table; existence, activity, capacity (SUM) and slot
        // availability are the handler's business — they need the database.
        RuleFor(x => x.ReservationData.CombinedTableIds)
            .Must(ids => ids == null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Combined tables must be distinct")
            .When(x => x.ReservationData != null);
        RuleFor(x => x.ReservationData.CombinedTableIds)
            .Must((command, ids) => ids == null || !ids.Contains(command.ReservationData.TableId))
            .WithMessage("A reservation cannot combine with its own primary table")
            .When(x => x.ReservationData != null);
    }
}
