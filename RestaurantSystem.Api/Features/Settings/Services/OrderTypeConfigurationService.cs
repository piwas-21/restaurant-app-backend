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
        await EnsureAllOrderTypesExistAsync(cancellationToken);

        var configurations = await _context.OrderTypeConfigurations
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

        return configurations.Select(c => new OrderTypeConfigurationDto
        {
            OrderType = c.OrderType,
            IsEnabled = c.IsEnabled,
            DisplayOrder = c.DisplayOrder,
            EnforceOpeningHours = c.EnforceOpeningHours
        }).ToList();
    }

    private async Task EnsureAllOrderTypesExistAsync(CancellationToken cancellationToken)
    {
        var existing = await _context.OrderTypeConfigurations
            .Select(c => c.OrderType)
            .ToListAsync(cancellationToken);

        var allTypes = Enum.GetValues<OrderType>();
        var missing = allTypes.Except(existing).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var auditIdentifier = _currentUserService.GetAuditIdentifier();
        foreach (var type in missing)
        {
            _context.OrderTypeConfigurations.Add(new OrderTypeConfiguration
            {
                OrderType = type,
                IsEnabled = true,
                DisplayOrder = (int)type,
                // A row that vanished (or a type added later) must come back with the gating that
                // type has ALWAYS had, not with a blanket on/off (#448).
                EnforceOpeningHours = OrderTypeConfiguration.EnforcedByDefault(type),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<OrderType>> GetEnabledOrderTypesAsync(CancellationToken cancellationToken = default)
    {
        var configurations = await _context.OrderTypeConfigurations
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

        var enabledTypes = configurations.Select(c => c.OrderType).ToList();

        // The opening-hours gate is PER ORDER TYPE (#448). DineIn has always been refused while
        // the restaurant is closed; takeaway and delivery keep the behaviour tenants rely on —
        // accepted at any hour — until the tenant turns the gate on for that type. Refusal looks
        // exactly as it always has for DineIn: the type is removed from the offered set.
        // One clock read decides every gated type at once: the answer cannot differ between them.
        if (configurations.Any(c => c.EnforceOpeningHours))
        {
            var isOpen = await _workingHoursService.IsOpenNowAsync(cancellationToken);

            if (!isOpen)
            {
                var gatedTypes = configurations
                    .Where(c => c.EnforceOpeningHours)
                    .Select(c => c.OrderType)
                    .ToHashSet();

                enabledTypes.RemoveAll(gatedTypes.Contains);
            }
        }

        return enabledTypes;
    }

    public async Task<OrderTypeConfigurationDto> UpdateAsync(
        OrderType orderType,
        bool isEnabled,
        bool? enforceOpeningHours = null,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _context.OrderTypeConfigurations
            .FirstOrDefaultAsync(c => c.OrderType == orderType, cancellationToken);

        if (configuration == null)
        {
            throw new NotFoundException($"Order type configuration not found for {orderType}");
        }

        configuration.IsEnabled = isEnabled;

        // null means "the caller has not heard of this field" — the shipped frontend sends only
        // { orderType, isEnabled }. Applying false there would switch the hours gate off on every
        // such save, so an omitted value must leave the stored one untouched.
        if (enforceOpeningHours.HasValue)
        {
            configuration.EnforceOpeningHours = enforceOpeningHours.Value;
        }

        configuration.UpdatedAt = DateTime.UtcNow;
        configuration.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return new OrderTypeConfigurationDto
        {
            OrderType = configuration.OrderType,
            IsEnabled = configuration.IsEnabled,
            DisplayOrder = configuration.DisplayOrder,
            EnforceOpeningHours = configuration.EnforceOpeningHours
        };
    }
}
