using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationQuery;

public sealed class GetCustomerReservationQueryValidator : AbstractValidator<GetCustomerReservationQuery>
{
    public GetCustomerReservationQueryValidator()
    {
        RuleFor(query => query.SchemaVersion).NotNull().Equal(1);
        RuleFor(query => query.ReservationId).NotEmpty();
    }
}
