using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationAvailabilityQuery;

public sealed class GetCustomerReservationAvailabilityQueryValidator : AbstractValidator<GetCustomerReservationAvailabilityQuery>
{
    public GetCustomerReservationAvailabilityQueryValidator()
    {
        RuleFor(query => query.SchemaVersion).NotNull().Equal(1);
        RuleFor(query => query.Date).NotNull();
        // Same party-size range as the existing ReservationWriteDto contract.
        RuleFor(query => query.NumberOfGuests).NotNull().InclusiveBetween(1, 20);
    }
}
