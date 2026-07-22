using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.FormFields.Dtos;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Settings.FormFields.Services;

public class FormFieldConfigurationService : IFormFieldConfigurationService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public FormFieldConfigurationService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _context.CustomerFormFieldConfigurations
            .Select(c => new { c.FormKey, c.FieldKey })
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(c => (c.FormKey, c.FieldKey)).ToHashSet();

        var missing = FormFieldRegistry.Fields
            .Where(f => !existingKeys.Contains((f.FormKey, f.FieldKey)))
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var auditIdentifier = _currentUserService.GetAuditIdentifier();
        foreach (var definition in missing)
        {
            _context.CustomerFormFieldConfigurations.Add(new CustomerFormFieldConfiguration
            {
                FormKey = definition.FormKey,
                FieldKey = definition.FieldKey,
                IsVisible = definition.DefaultIsVisible,
                IsRequired = definition.DefaultIsRequired,
                DisplayOrder = definition.DisplayOrder,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<FormFieldsDto>> GetGroupedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);

        var rows = await _context.CustomerFormFieldConfigurations
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var byKey = rows.ToDictionary(r => (r.FormKey, r.FieldKey));

        // The registry drives shape and order; DB rows only contribute the admin's
        // visible/required choices. Locked fields are forced visible + required so a
        // stray row can never break the flow.
        return FormFieldRegistry.Fields
            .GroupBy(f => f.FormKey)
            .Select(group => new FormFieldsDto(
                group.Key,
                group
                    .OrderBy(f => f.DisplayOrder)
                    .Select(f =>
                    {
                        var row = byKey.GetValueOrDefault((f.FormKey, f.FieldKey));
                        return new FormFieldConfigurationDto(
                            f.FieldKey,
                            f.IsLocked || (row?.IsVisible ?? f.DefaultIsVisible),
                            f.IsLocked || (row?.IsRequired ?? f.DefaultIsRequired),
                            f.IsLocked,
                            f.DisplayOrder);
                    })
                    .ToList()))
            .ToList();
    }
}
