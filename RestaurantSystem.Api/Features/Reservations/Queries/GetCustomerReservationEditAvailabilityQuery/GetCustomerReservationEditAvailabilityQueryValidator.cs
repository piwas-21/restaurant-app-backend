using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationEditAvailabilityQuery;

public sealed class GetCustomerReservationEditAvailabilityQueryValidator : AbstractValidator<GetCustomerReservationEditAvailabilityQuery>
{
    public GetCustomerReservationEditAvailabilityQueryValidator()
    {
        RuleFor(query => query.SchemaVersion).NotNull().Equal(1);
        RuleFor(query => query.ReservationId).NotEmpty();
        RuleFor(query => query.Date).NotNull();
        RuleFor(query => query.NumberOfGuests).NotNull().InclusiveBetween(1, 20);
        RuleFor(query => query.TimeZone).NotEmpty().MaximumLength(128);
    }
}
