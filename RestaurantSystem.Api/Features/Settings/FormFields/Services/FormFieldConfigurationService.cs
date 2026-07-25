using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    private readonly IFormFieldSeedState _seedState;

    public FormFieldConfigurationService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IFormFieldSeedState seedState)
    {
        _context = context;
        _currentUserService = currentUserService;
        _seedState = seedState;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (_seedState.IsSeeded)
        {
            return;
        }

        var existing = await _context.CustomerFormFieldConfigurations
            .Select(c => new { c.FormKey, c.FieldKey })
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(c => (c.FormKey, c.FieldKey)).ToHashSet();

        var missing = FormFieldRegistry.Fields
            .Where(f => !existingKeys.Contains((f.FormKey, f.FieldKey)))
            .ToList();
        if (missing.Count == 0)
        {
            _seedState.MarkSeeded();
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

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent first request seeded the same rows and won the unique
            // (form_key, field_key) index race — its rows are exactly what we were
            // inserting. Drop our tracked duplicates so this context stays usable for
            // the read that follows. (The OrderTypeConfiguration precedent this
            // follows has no unique index, so this race is specific to this table.)
            _context.ChangeTracker.Clear();
        }

        _seedState.MarkSeeded();
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
