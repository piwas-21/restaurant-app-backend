using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Addresses.Queries.GetCustomerAddressesQuery;

public sealed record GetCustomerAddressesQuery(int? SchemaVersion, Guid? AfterId = null)
    : IQuery<ApiResponse<CustomerAddressPageDto>>;

public sealed class GetCustomerAddressesQueryHandler(ApplicationDbContext context, ICurrentUserService currentUser)
    : IQueryHandler<GetCustomerAddressesQuery, ApiResponse<CustomerAddressPageDto>>
{
    private const int PageSize = 20;
    public async Task<ApiResponse<CustomerAddressPageDto>> Handle(GetCustomerAddressesQuery query,
        CancellationToken cancellationToken)
    {
        var owner = CustomerAccountAccess.RequireOwner(currentUser);
        // A single bounded projection keeps the current customer predicate and private rows in
        // the same database read. The cursor is navigation only and never selects the owner.
        var account = await CustomerAccountAccess.Current(context, currentUser).AsNoTracking()
            .Select(user => new
            {
                Items = context.UserAddresses.Where(address => address.UserId == user.Id && address.UserId == owner && !address.IsDeleted
                        && (!query.AfterId.HasValue || address.Id.CompareTo(query.AfterId.Value) > 0))
                    .OrderBy(address => address.Id).Take(PageSize + 1)
                    .Select(address => new CustomerAddressDto(address.Id, address.Label, address.AddressLine1,
                        address.AddressLine2, address.City, address.State, address.PostalCode, address.Country,
                        address.Phone, address.IsDefault, address.DeliveryInstructions)).ToList()
            }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Customer account unavailable.");
        var items = account.Items.Take(PageSize).ToArray();
        Guid? next = account.Items.Count > PageSize ? items[^1].Id : null;
        return ApiResponse<CustomerAddressPageDto>.SuccessWithData(new(1, items, next));
    }
}
