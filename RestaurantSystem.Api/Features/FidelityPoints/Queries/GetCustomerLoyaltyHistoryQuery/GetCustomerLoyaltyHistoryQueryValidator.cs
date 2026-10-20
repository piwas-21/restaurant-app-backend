using FluentValidation;

namespace RestaurantSystem.Api.Features.FidelityPoints.Queries.GetCustomerLoyaltyHistoryQuery;

public sealed class GetCustomerLoyaltyHistoryQueryValidator : AbstractValidator<GetCustomerLoyaltyHistoryQuery>
{
    public GetCustomerLoyaltyHistoryQueryValidator()
    {
        RuleFor(query => query.SchemaVersion).NotNull().Equal(1);
        RuleFor(query => query).Must(query => query.BeforeCreatedAt.HasValue == query.BeforeId.HasValue)
            .WithMessage("Provide the complete loyalty cursor.");
        RuleFor(query => query.BeforeId).NotEqual(Guid.Empty).When(query => query.BeforeId.HasValue);
        RuleFor(query => query.BeforeCreatedAt).Must(value => value is null || value.Value.Kind == DateTimeKind.Utc)
            .WithMessage("Use the UTC loyalty cursor returned by the server.");
    }
}
