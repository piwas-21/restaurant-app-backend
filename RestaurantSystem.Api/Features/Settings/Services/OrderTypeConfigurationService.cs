using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Settings.Services;

public class OrderTypeConfigurationService : IOrderTypeConfigurationService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IWorkingHoursService _workingHoursService;

    public OrderTypeConfigurationService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IWorkingHoursService workingHoursService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _workingHoursService = workingHoursService;
    }

    public async Task<List<OrderTypeConfigurationDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var configurations = await _context.OrderTypeConfigurations
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

        return configurations.Select(c => new OrderTypeConfigurationDto
        {
            OrderType = c.OrderType,
            IsEnabled = c.IsEnabled,
            DisplayOrder = c.DisplayOrder
        }).ToList();
    }

    public async Task<List<OrderType>> GetEnabledOrderTypesAsync(CancellationToken cancellationToken = default)
    {
        var enabledTypes = await _context.OrderTypeConfigurations
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => c.OrderType)
            .ToListAsync(cancellationToken);

        // Check if restaurant is currently open for dine-in using dynamic working hours
        if (enabledTypes.Contains(OrderType.DineIn))
        {
            var isOpen = await _workingHoursService.IsOpenNowAsync(cancellationToken);

            // Remove dine-in if restaurant is closed
            if (!isOpen)
            {
                enabledTypes.Remove(OrderType.DineIn);
            }
        }

        return enabledTypes;
    }

    public async Task<OrderTypeConfigurationDto> UpdateAsync(
        OrderType orderType,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _context.OrderTypeConfigurations
            .FirstOrDefaultAsync(c => c.OrderType == orderType, cancellationToken);

        if (configuration == null)
        {
            throw new NotFoundException($"Order type configuration not found for {orderType}");
        }

        configuration.IsEnabled = isEnabled;
        configuration.UpdatedAt = DateTime.UtcNow;
        configuration.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return new OrderTypeConfigurationDto
        {
            OrderType = configuration.OrderType,
            IsEnabled = configuration.IsEnabled,
            DisplayOrder = configuration.DisplayOrder
        };
    }
}
