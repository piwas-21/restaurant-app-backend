using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Settings.FormFields.Services;

public class FormFieldRequirementService : IFormFieldRequirementService
{
    private readonly ApplicationDbContext _context;

    public FormFieldRequirementService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task EnsureRequiredFieldsPresentAsync(
        string formKey,
        IReadOnlyDictionary<string, string?> fieldValues,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.CustomerFormFieldConfigurations
            .AsNoTracking()
            .Where(c => c.FormKey == formKey)
            .ToListAsync(cancellationToken);
        var requiredByKey = rows.ToDictionary(r => r.FieldKey, r => r.IsRequired);

        var missing = FormFieldRegistry.Fields
            .Where(f => f.FormKey == formKey
                && fieldValues.ContainsKey(f.FieldKey)
                && requiredByKey.GetValueOrDefault(f.FieldKey, f.DefaultIsRequired)
                && string.IsNullOrWhiteSpace(fieldValues[f.FieldKey]))
            .Select(f => f.FieldKey)
            .ToList();

        if (missing.Count > 0)
        {
            throw new BadRequestException(
                $"Required field(s) missing: {string.Join(", ", missing)}");
        }
    }
}
