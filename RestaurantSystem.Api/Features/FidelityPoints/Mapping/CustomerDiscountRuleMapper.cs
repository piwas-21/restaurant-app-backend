using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Mapping;

/// <summary>
/// Single source of truth for the <see cref="CustomerDiscountRule"/> →
/// <see cref="CustomerDiscountRuleDto"/> projection.
///
/// The DTO shape (and its user email/name enrichment fallbacks) was previously
/// duplicated verbatim across the controller's GetAll / GetById / Create /
/// Update actions. Centralising it here keeps those four call sites — and the
/// exact "Unknown" fallbacks — in one place. The two enrichment helpers
/// deliberately preserve the original per-shape semantics:
///   • list shape  (GetAll):        missing user → email/name = "Unknown";
///                                   present user → email = <c>Email ?? ""</c>.
///   • single shape (GetById/…):    missing user → email/name = "Unknown";
///                                   present user → email = <c>Email ?? "Unknown"</c>.
/// </summary>
public static class CustomerDiscountRuleMapper
{
    /// <summary>Display fallback for a missing user or a user with no email (matches the original inline behaviour).</summary>
    private const string UnknownUser = "Unknown";

    /// <summary>
    /// Core field-for-field projection. Callers supply the already-resolved
    /// display <paramref name="userEmail"/> / <paramref name="userName"/>.
    /// </summary>
    public static CustomerDiscountRuleDto ToDto(CustomerDiscountRule discount, string userEmail, string userName)
    {
        return new CustomerDiscountRuleDto
        {
            Id = discount.Id,
            UserId = discount.UserId,
            UserEmail = userEmail,
            UserName = userName,
            Name = discount.Name,
            DiscountType = discount.DiscountType.ToString(),
            DiscountValue = discount.DiscountValue,
            MinOrderAmount = discount.MinOrderAmount,
            MaxOrderAmount = discount.MaxOrderAmount,
            MaxUsageCount = discount.MaxUsageCount,
            UsageCount = discount.UsageCount,
            IsActive = discount.IsActive,
            ValidFrom = discount.ValidFrom,
            ValidUntil = discount.ValidUntil,
            CreatedAt = discount.CreatedAt
        };
    }

    /// <summary>
    /// Enriches from a batch user lookup (original GetAll semantics). A missing
    /// user maps to <c>("Unknown", "Unknown", "")</c> → <c>UserEmail = "Unknown"</c>,
    /// <c>UserName = "Unknown"</c>; a present user with a null email maps to
    /// <c>UserEmail = string.Empty</c>.
    /// </summary>
    public static CustomerDiscountRuleDto ToDtoFromLookup(
        CustomerDiscountRule discount,
        IReadOnlyDictionary<Guid, (string? Email, string FirstName, string LastName)> userLookup)
    {
        if (userLookup.TryGetValue(discount.UserId, out var user))
        {
            return ToDto(discount, user.Email ?? string.Empty, $"{user.FirstName} {user.LastName}".Trim());
        }

        return ToDto(discount, UnknownUser, UnknownUser);
    }

    /// <summary>
    /// Enriches from a single (nullable) user projection (original GetById /
    /// Create / Update semantics). A missing user maps to
    /// <c>UserEmail = "Unknown"</c>, <c>UserName = "Unknown"</c>; a present user
    /// with a null email maps to <c>UserEmail = "Unknown"</c>.
    /// </summary>
    public static CustomerDiscountRuleDto ToDtoFromUser(
        CustomerDiscountRule discount,
        (string? Email, string FirstName, string LastName)? user)
    {
        if (user is null)
        {
            return ToDto(discount, UnknownUser, UnknownUser);
        }

        var value = user.Value;
        return ToDto(discount, value.Email ?? UnknownUser, $"{value.FirstName} {value.LastName}".Trim());
    }

    /// <summary>
    /// Builds a new <see cref="CustomerDiscountRule"/> from a create request.
    /// The <c>CreatedBy</c> placeholder is overwritten with the audited identity
    /// by the persistence layer on save (mirrors the original controller).
    /// </summary>
    public static CustomerDiscountRule ToEntity(CreateCustomerDiscountRuleDto dto, DiscountType discountType)
    {
        return new CustomerDiscountRule
        {
            UserId = dto.UserId,
            Name = dto.Name,
            DiscountType = discountType,
            DiscountValue = dto.DiscountValue,
            MinOrderAmount = dto.MinOrderAmount,
            MaxOrderAmount = dto.MaxOrderAmount,
            MaxUsageCount = dto.MaxUsageCount,
            IsActive = dto.IsActive,
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            CreatedBy = "System"
        };
    }

    /// <summary>
    /// Builds a <see cref="CustomerDiscountRule"/> carrying the fields an update
    /// applies to the persisted rule identified by <paramref name="id"/>
    /// (<c>UserId</c> is intentionally not part of an update).
    /// </summary>
    public static CustomerDiscountRule ToEntity(Guid id, UpdateCustomerDiscountRuleDto dto, DiscountType discountType)
    {
        return new CustomerDiscountRule
        {
            Id = id,
            Name = dto.Name,
            DiscountType = discountType,
            DiscountValue = dto.DiscountValue,
            MinOrderAmount = dto.MinOrderAmount,
            MaxOrderAmount = dto.MaxOrderAmount,
            MaxUsageCount = dto.MaxUsageCount,
            IsActive = dto.IsActive,
            ValidFrom = dto.ValidFrom,
            ValidUntil = dto.ValidUntil,
            CreatedBy = "System"
        };
    }
}
