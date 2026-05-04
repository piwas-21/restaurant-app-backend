using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Settings.Services;

public class TaxConfigurationService : ITaxConfigurationService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public TaxConfigurationService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<TaxConfiguration?> GetActiveTaxConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TaxConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IsEnabled, cancellationToken);
    }

    public async Task<TaxConfiguration?> GetTaxConfigurationByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TaxConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<TaxConfiguration?> GetTaxConfigurationByOrderTypeAsync(OrderType orderType, CancellationToken cancellationToken = default)
    {
        var orderTypeValue = ((int)orderType).ToString();

        return await _context.TaxConfigurations
            .AsNoTracking()
            .Where(t => t.IsEnabled &&
                       (t.ApplicableOrderTypes.Contains(orderTypeValue) || string.IsNullOrEmpty(t.ApplicableOrderTypes)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<TaxConfiguration>> GetAllTaxConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TaxConfigurations
            .AsNoTracking()
            .OrderByDescending(t => t.IsEnabled)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<TaxConfiguration> CreateTaxConfigurationAsync(TaxConfiguration taxConfiguration, CancellationToken cancellationToken = default)
    {
        taxConfiguration.CreatedAt = DateTime.UtcNow;
        taxConfiguration.CreatedBy = _currentUserService.GetAuditIdentifier();

        _context.TaxConfigurations.Add(taxConfiguration);
        await _context.SaveChangesAsync(cancellationToken);

        return taxConfiguration;
    }

    public async Task<TaxConfiguration> UpdateTaxConfigurationAsync(TaxConfiguration taxConfiguration, CancellationToken cancellationToken = default)
    {
        var existing = await _context.TaxConfigurations
            .FirstOrDefaultAsync(t => t.Id == taxConfiguration.Id, cancellationToken);

        if (existing == null)
            throw new NotFoundException($"Tax configuration with ID {taxConfiguration.Id} not found");

        existing.Name = taxConfiguration.Name;
        existing.Rate = taxConfiguration.Rate;
        existing.IsEnabled = taxConfiguration.IsEnabled;
        existing.Description = taxConfiguration.Description;
        existing.ApplicableOrderTypes = taxConfiguration.ApplicableOrderTypes;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return existing;
    }

    public async Task DeleteTaxConfigurationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var taxConfiguration = await _context.TaxConfigurations
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (taxConfiguration == null)
            throw new NotFoundException($"Tax configuration with ID {id} not found");

        _context.TaxConfigurations.Remove(taxConfiguration);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<decimal> CalculateTaxAsync(decimal amount, CancellationToken cancellationToken = default)
    {
        var activeTax = await GetActiveTaxConfigurationAsync(cancellationToken);

        if (activeTax == null || !activeTax.IsEnabled)
            return 0;

        // Rate is stored as percentage (e.g., 8.1 for 8.1%), so divide by 100
        return Math.Round(amount * activeTax.Rate / 100, 2);
    }

    public async Task<decimal> CalculateTaxByOrderTypeAsync(decimal amount, OrderType orderType, CancellationToken cancellationToken = default)
    {
        var tax = await GetTaxConfigurationByOrderTypeAsync(orderType, cancellationToken);

        if (tax == null || !tax.IsEnabled)
            return 0;

        // Rate is stored as percentage (e.g., 8.1 for 8.1%), so divide by 100
        return Math.Round(amount * tax.Rate / 100, 2);
    }
}
