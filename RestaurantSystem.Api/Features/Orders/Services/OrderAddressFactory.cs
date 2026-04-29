using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderAddressFactory : IOrderAddressFactory
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<OrderAddressFactory> _logger;

    public OrderAddressFactory(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<OrderAddressFactory> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<OrderAddress?> CreateAsync(
        CreateOrderDeliveryAddressDto? addressDto,
        Guid orderId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        // Case 1: client passed UseAddressId — copy from the saved UserAddress.
        if (addressDto?.UseAddressId != null)
        {
            var savedAddress = await _context.UserAddresses
                .FirstOrDefaultAsync(
                    a => a.Id == addressDto.UseAddressId && !a.IsDeleted,
                    cancellationToken);

            if (savedAddress != null)
            {
                return SnapshotFromUserAddress(savedAddress, orderId);
            }
        }

        // Case 2: client passed inline address fields — use them verbatim.
        if (addressDto != null && !string.IsNullOrEmpty(addressDto.AddressLine1))
        {
            return new OrderAddress
            {
                OrderId = orderId,
                Label = addressDto.Label ?? "Delivery Address",
                AddressLine1 = addressDto.AddressLine1,
                AddressLine2 = addressDto.AddressLine2,
                City = addressDto.City!,
                State = addressDto.State,
                PostalCode = addressDto.PostalCode!,
                Country = addressDto.Country!,
                Phone = addressDto.Phone,
                Latitude = addressDto.Latitude,
                Longitude = addressDto.Longitude,
                DeliveryInstructions = addressDto.DeliveryInstructions,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier(),
            };
        }

        // Case 3: no usable DTO — fall back to the customer's default address.
        if (userId.HasValue)
        {
            var defaultAddress = await _context.UserAddresses
                .FirstOrDefaultAsync(
                    a => a.UserId == userId && a.IsDefault && !a.IsDeleted,
                    cancellationToken);

            if (defaultAddress != null)
            {
                _logger.LogInformation("Using customer's default address for order");
                return SnapshotFromUserAddress(defaultAddress, orderId);
            }
        }

        return null;
    }

    // Cases 1 and 3 share the same field-by-field copy from a saved
    // UserAddress — DRY'd into one helper.
    private OrderAddress SnapshotFromUserAddress(UserAddress source, Guid orderId) => new()
    {
        OrderId = orderId,
        UserAddressId = source.Id,
        Label = source.Label,
        AddressLine1 = source.AddressLine1,
        AddressLine2 = source.AddressLine2,
        City = source.City,
        State = source.State,
        PostalCode = source.PostalCode,
        Country = source.Country,
        Phone = source.Phone,
        Latitude = source.Latitude,
        Longitude = source.Longitude,
        DeliveryInstructions = source.DeliveryInstructions,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = _currentUserService.GetAuditIdentifier(),
    };
}
