using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Settings.Interfaces;

public interface ITaxConfigurationService
{
    Task<TaxConfiguration?> GetActiveTaxConfigurationAsync(CancellationToken cancellationToken = default);
    Task<TaxConfiguration?> GetTaxConfigurationByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TaxConfiguration?> GetTaxConfigurationByOrderTypeAsync(OrderType orderType, CancellationToken cancellationToken = default);
    Task<List<TaxConfiguration>> GetAllTaxConfigurationsAsync(CancellationToken cancellationToken = default);
    Task<TaxConfiguration> CreateTaxConfigurationAsync(TaxConfiguration taxConfiguration, CancellationToken cancellationToken = default);
    Task<TaxConfiguration> UpdateTaxConfigurationAsync(TaxConfiguration taxConfiguration, CancellationToken cancellationToken = default);
    Task DeleteTaxConfigurationAsync(Guid id, CancellationToken cancellationToken = default);
    Task<decimal> CalculateTaxAsync(decimal amount, CancellationToken cancellationToken = default);
    Task<decimal> CalculateTaxByOrderTypeAsync(decimal amount, OrderType orderType, CancellationToken cancellationToken = default);
}
