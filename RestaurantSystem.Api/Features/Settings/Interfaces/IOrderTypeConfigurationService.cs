using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Settings.Interfaces;

public interface IOrderTypeConfigurationService
{
    Task<List<OrderTypeConfigurationDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<OrderType>> GetEnabledOrderTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The confirmation behaviour a guest screen may read without an account: flow + review window
    /// per order type. Deliberately projected — the admin DTO carries nothing a guest needs.
    /// </summary>
    Task<List<OrderTypeConfirmationPublicDto>> GetPublicConfirmationConfigurationsAsync(CancellationToken cancellationToken = default);

    Task<OrderTypeConfigurationDto> UpdateAsync(
        OrderType orderType,
        bool isEnabled,
        bool? enforceOpeningHours = null,
        string? confirmationFlow = null,
        int? reviewWindowMinutes = null,
        CancellationToken cancellationToken = default);
}
