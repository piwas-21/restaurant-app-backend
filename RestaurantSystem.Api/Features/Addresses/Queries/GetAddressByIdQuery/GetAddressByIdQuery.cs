using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Addresses.Queries.GetAddressByIdQuery;

public record GetAddressByIdQuery(Guid Id) : IQuery<ApiResponse<AddressDto>>;

public class GetAddressByIdQueryHandler : IQueryHandler<GetAddressByIdQuery, ApiResponse<AddressDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetAddressByIdQueryHandler> _logger;

    public GetAddressByIdQueryHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<GetAddressByIdQueryHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<AddressDto>> Handle(GetAddressByIdQuery query, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return ApiResponse<AddressDto>.Failure("User not authenticated");
        }

        var address = await _context.UserAddresses
            .Where(a => a.Id == query.Id && a.UserId == userId.Value && !a.IsDeleted)
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
            .FirstOrDefaultAsync(cancellationToken);

        if (address == null)
        {
            return ApiResponse<AddressDto>.Failure("Address not found");
        }

        return ApiResponse<AddressDto>.SuccessWithData(address);
    }
}
