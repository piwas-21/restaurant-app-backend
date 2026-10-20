using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationEditContextQuery;

public sealed class GetCustomerReservationEditContextQueryValidator : AbstractValidator<GetCustomerReservationEditContextQuery>
{
    public GetCustomerReservationEditContextQueryValidator()
    {
        RuleFor(query => query.SchemaVersion).NotNull().Equal(1);
        RuleFor(query => query.ReservationId).NotEmpty();
    }
}
