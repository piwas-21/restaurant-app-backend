using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationsQuery;

public sealed class GetCustomerReservationsQueryValidator : AbstractValidator<GetCustomerReservationsQuery>
{
    public GetCustomerReservationsQueryValidator()
    {
        RuleFor(query => query.SchemaVersion).NotNull().Equal(1);
        RuleFor(query => query).Must(query =>
            query.BeforeDate.HasValue == query.BeforeStartTime.HasValue
            && query.BeforeDate.HasValue == query.BeforeId.HasValue).WithMessage("Provide the complete reservation cursor.");
        RuleFor(query => query.BeforeId).NotEqual(Guid.Empty).When(query => query.BeforeId.HasValue);
        RuleFor(query => query.BeforeDate).Must(value => value is null || value.Value.Kind == DateTimeKind.Utc)
            .WithMessage("Use the UTC reservation cursor returned by the server.");
    }
}
