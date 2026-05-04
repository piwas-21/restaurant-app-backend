using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Addresses.Queries.GetUserAddressesQuery;

public record GetUserAddressesQuery : IQuery<ApiResponse<List<AddressDto>>>;

public class GetUserAddressesQueryHandler : IQueryHandler<GetUserAddressesQuery, ApiResponse<List<AddressDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetUserAddressesQueryHandler> _logger;

    public GetUserAddressesQueryHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<GetUserAddressesQueryHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<List<AddressDto>>> Handle(GetUserAddressesQuery query, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return ApiResponse<List<AddressDto>>.Failure("User not authenticated");
        }

        var addresses = await _context.UserAddresses
            .Where(a => a.UserId == userId.Value && !a.IsDeleted)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new AddressDto
            {
                Id = a.Id,
                UserId = a.UserId,
                Label = a.Label,
                AddressLine1 = a.AddressLine1,
                AddressLine2 = a.AddressLine2,
                City = a.City,
                State = a.State,
                PostalCode = a.PostalCode,
                Country = a.Country,
                Phone = a.Phone,
                IsDefault = a.IsDefault,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                DeliveryInstructions = a.DeliveryInstructions,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<List<AddressDto>>.SuccessWithData(addresses);
    }
}
