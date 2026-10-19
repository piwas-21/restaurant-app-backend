using FluentValidation;

namespace RestaurantSystem.Api.Features.Addresses.Queries.GetCustomerAddressesQuery;

public sealed class GetCustomerAddressesQueryValidator : AbstractValidator<GetCustomerAddressesQuery>
{
    public GetCustomerAddressesQueryValidator()
    {
        RuleFor(value => value.SchemaVersion).NotNull().Equal(1);
        RuleFor(value => value.AfterId).NotEqual(Guid.Empty).When(value => value.AfterId.HasValue);
    }
}
