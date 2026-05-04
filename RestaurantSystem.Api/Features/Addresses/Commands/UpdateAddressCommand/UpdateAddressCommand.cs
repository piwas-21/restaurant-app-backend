using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Addresses.Commands.UpdateAddressCommand;

public record UpdateAddressCommand : ICommand<ApiResponse<AddressDto>>
{
    public Guid Id { get; set; }
    public string Label { get; set; } = null!;
    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = null!;
    public string? State { get; set; }
    public string PostalCode { get; set; } = null!;
    public string Country { get; set; } = null!;
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? DeliveryInstructions { get; set; }
}

public class UpdateAddressCommandHandler : ICommandHandler<UpdateAddressCommand, ApiResponse<AddressDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateAddressCommandHandler> _logger;

    public UpdateAddressCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<UpdateAddressCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<AddressDto>> Handle(UpdateAddressCommand command, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return ApiResponse<AddressDto>.Failure("User not authenticated");
        }

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var address = await _context.UserAddresses
                .FirstOrDefaultAsync(a => a.Id == command.Id && a.UserId == userId.Value && !a.IsDeleted, cancellationToken);

            if (address == null)
            {
                return ApiResponse<AddressDto>.Failure("Address not found");
            }

            // If setting as default, unset other defaults
            if (command.IsDefault && !address.IsDefault)
            {
                await _context.UserAddresses
                    .Where(a => a.UserId == userId.Value && a.IsDefault && a.Id != command.Id && !a.IsDeleted)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), cancellationToken);
            }

            // Update address
            address.Label = command.Label;
            address.AddressLine1 = command.AddressLine1;
            address.AddressLine2 = command.AddressLine2;
            address.City = command.City;
            address.State = command.State;
            address.PostalCode = command.PostalCode;
            address.Country = command.Country;
            address.Phone = command.Phone;
            address.IsDefault = command.IsDefault;
            address.Latitude = command.Latitude;
            address.Longitude = command.Longitude;
            address.DeliveryInstructions = command.DeliveryInstructions;
            address.UpdatedAt = DateTime.UtcNow;
            address.UpdatedBy = userId.Value.ToString();

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

            _logger.LogInformation("Address {AddressId} updated successfully for user {UserId}", command.Id, userId.Value);
            return ApiResponse<AddressDto>.SuccessWithData(dto, "Address updated successfully");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error updating address {AddressId} for user {UserId}", command.Id, userId.Value);
            throw;
        }
    }
}
