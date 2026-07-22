using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.FormFields.Dtos;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Settings.FormFields.Commands.UpdateFormFieldConfigurationsCommand;

/// <summary>
/// Bulk update of configurable customer form fields (admin). Locked fields may only be
/// echoed unchanged (visible + required); unknown pairs, locked-field changes and
/// required-but-hidden combinations are rejected with 400.
/// </summary>
public record UpdateFormFieldConfigurationsCommand(List<UpdateFormFieldConfigurationDto> Fields)
    : ICommand<ApiResponse<List<FormFieldsDto>>>;

public class UpdateFormFieldConfigurationsCommandHandler
    : ICommandHandler<UpdateFormFieldConfigurationsCommand, ApiResponse<List<FormFieldsDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly IFormFieldConfigurationService _configurationService;
    private readonly ICurrentUserService _currentUserService;

    public UpdateFormFieldConfigurationsCommandHandler(
        ApplicationDbContext context,
        IFormFieldConfigurationService configurationService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _configurationService = configurationService;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<List<FormFieldsDto>>> Handle(
        UpdateFormFieldConfigurationsCommand command, CancellationToken cancellationToken)
    {
        ValidateAgainstRegistry(command.Fields);

        await _configurationService.EnsureSeededAsync(cancellationToken);

        var rows = await _context.CustomerFormFieldConfigurations
            .ToListAsync(cancellationToken);
        var byKey = rows.ToDictionary(r => (r.FormKey, r.FieldKey));
        var auditIdentifier = _currentUserService.GetAuditIdentifier();

        foreach (var field in command.Fields)
        {
            var row = byKey[(field.FormKey, field.FieldKey)];
            if (row.IsVisible == field.IsVisible && row.IsRequired == field.IsRequired)
            {
                continue;
            }

            row.IsVisible = field.IsVisible;
            row.IsRequired = field.IsRequired;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = auditIdentifier;
        }

        await _context.SaveChangesAsync(cancellationToken);

        var forms = await _configurationService.GetGroupedAsync(cancellationToken);
        return ApiResponse<List<FormFieldsDto>>.SuccessWithData(
            forms, "Customer form fields updated successfully");
    }

    private static void ValidateAgainstRegistry(List<UpdateFormFieldConfigurationDto> fields)
    {
        var duplicate = fields
            .GroupBy(f => (f.FormKey, f.FieldKey))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new BadRequestException(
                $"Duplicate entry for field '{duplicate.Key.FormKey}.{duplicate.Key.FieldKey}'");
        }

        foreach (var field in fields)
        {
            var definition = FormFieldRegistry.Find(field.FormKey, field.FieldKey)
                ?? throw new BadRequestException(
                    $"Unknown form field '{field.FormKey}.{field.FieldKey}'");

            if (definition.IsLocked && (!field.IsVisible || !field.IsRequired))
            {
                throw new BadRequestException(
                    $"Field '{field.FormKey}.{field.FieldKey}' is locked and must stay visible and required");
            }

            if (field.IsRequired && !field.IsVisible)
            {
                throw new BadRequestException(
                    $"Field '{field.FormKey}.{field.FieldKey}' cannot be required while hidden");
            }
        }
    }
}
