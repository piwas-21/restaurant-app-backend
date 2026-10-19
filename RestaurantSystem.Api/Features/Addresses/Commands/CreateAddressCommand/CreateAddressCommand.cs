using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Services;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Addresses.Commands.CreateAddressCommand;

public record CreateAddressCommand : AddressContent, ICommand<ApiResponse<AddressDto>>;

public class CreateAddressCommandHandler : ICommandHandler<CreateAddressCommand, ApiResponse<AddressDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateAddressCommandHandler> _logger;

    public CreateAddressCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<CreateAddressCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<AddressDto>> Handle(CreateAddressCommand command, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return ApiResponse<AddressDto>.Failure("User not authenticated");
        }

        var isDefault = command.IsDefault ?? false;
        await using var transaction = await AddressWriteTransaction.BeginAsync(_context, userId.Value, cancellationToken, command.RequireCustomer);

        try
        {
            // If this is set as default, unset other default addresses
            if (isDefault)
            {
                await _context.UserAddresses
                    .Where(a => a.UserId == userId.Value && a.IsDefault && !a.IsDeleted)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), cancellationToken);
            }

            var address = new UserAddress
            {
                UserId = userId.Value,
                Label = command.Label,
                AddressLine1 = command.AddressLine1,
                AddressLine2 = command.AddressLine2,
                City = command.City,
                State = command.State,
                PostalCode = command.PostalCode,
                Country = command.Country,
                Phone = command.Phone,
                IsDefault = isDefault,
                Latitude = command.Latitude,
                Longitude = command.Longitude,
                DeliveryInstructions = command.DeliveryInstructions,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId.Value.ToString()
            };

            _context.UserAddresses.Add(address);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var dto = new AddressDto
            {
                Id = address.Id,
                UserId = address.UserId,
                Label = address.Label,
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                City = address.City,
                State = address.State,
                PostalCode = address.PostalCode,
                Country = address.Country,
                Phone = address.Phone,
                IsDefault = address.IsDefault,
                Latitude = address.Latitude,
                Longitude = address.Longitude,
                DeliveryInstructions = address.DeliveryInstructions,
                CreatedAt = address.CreatedAt,
                UpdatedAt = address.UpdatedAt
            };

            _logger.LogInformation("Address created successfully for user {UserId}", userId.Value);
            return ApiResponse<AddressDto>.SuccessWithData(dto, "Address created successfully");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating address for user {UserId}", userId.Value);
            throw;
        }
    }
}
