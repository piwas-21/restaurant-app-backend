using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed partial class CatalogueRevisionLocalTextStore
{
    private void Touch(BaseEntity entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.GetAuditIdentifier();
    }

    private bool SetText(
        BaseEntity entity,
        string? current,
        string? value,
        Action<string?> setter,
        string label,
        int? maxLength)
    {
        ValidateText(value, label, maxLength, required: label.EndsWith("name", StringComparison.Ordinal));
        if (string.Equals(current, value, StringComparison.Ordinal)) return false;
        setter(value);
        Touch(entity);
        return true;
    }

    private static void ValidateText(string? value, string label, int? maxLength, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || (maxLength is int maximum && value?.Length > maximum))
        {
            throw new ConflictException($"The published {label} cannot be applied to the tenant record.");
        }
    }
}
