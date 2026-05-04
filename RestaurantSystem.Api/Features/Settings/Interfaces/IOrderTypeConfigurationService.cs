using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Settings.Interfaces;

public interface IOrderTypeConfigurationService
{
    Task<List<OrderTypeConfigurationDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<OrderType>> GetEnabledOrderTypesAsync(CancellationToken cancellationToken = default);
    Task<OrderTypeConfigurationDto> UpdateAsync(OrderType orderType, bool isEnabled, CancellationToken cancellationToken = default);
}
