using RestaurantSystem.Api.Features.Groups.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Groups.Mapping;

/// <summary>
/// Single source of truth for the Groups feature's entity → DTO projections.
///
/// The <see cref="UserGroup"/> → <see cref="UserGroupDto"/>,
/// <see cref="GroupDiscount"/> → <see cref="GroupDiscountDto"/> and
/// <see cref="GroupMembership"/> → <see cref="GroupMembershipDto"/> shapes were
/// previously duplicated verbatim across UserGroupService's Create / Update /
/// GetById / GetAll / AddMember / GetGroupMembers / ValidateQr paths. Centralising
/// them here keeps those call sites — and their exact field-for-field semantics —
/// in one place.
///
/// The user email/name for a membership is resolved by the caller (the source
/// differs per call site: a separately-loaded user on add, the <c>m.User</c>
/// navigation on list/validate) and passed in, preserving the original
/// <c>?? ""</c> fallbacks exactly.
/// </summary>
public static class UserGroupMapper
{
    /// <summary>Field-for-field <see cref="GroupDiscount"/> → <see cref="GroupDiscountDto"/> projection.</summary>
    public static GroupDiscountDto ToDto(GroupDiscount discount)
    {
        return new GroupDiscountDto
        {
            Id = discount.Id,
            GroupId = discount.GroupId,
            Name = discount.Name,
            Type = discount.Type,
            Value = discount.Value,
            MinimumOrderAmount = discount.MinimumOrderAmount,
            MaximumDiscountAmount = discount.MaximumDiscountAmount,
            IsActive = discount.IsActive
        };
    }

    /// <summary>
    /// Standard <see cref="UserGroup"/> → <see cref="UserGroupDto"/> projection
    /// (GetById / GetAll semantics): <c>MemberCount</c> comes from the loaded
    /// <c>Memberships</c> collection and <c>Discounts</c> maps every discount
    /// (no active-only filter). Callers must have loaded both navigations.
    /// </summary>
    public static UserGroupDto ToDto(UserGroup group)
    {
        return new UserGroupDto
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            QRCodeData = group.QRCodeData,
            IsActive = group.IsActive,
            ValidFrom = group.ValidFrom,
            ValidUntil = group.ValidUntil,
            MemberCount = group.Memberships.Count,
            Discounts = group.Discounts.Select(ToDto).ToList()
        };
    }

    /// <summary>
    /// <see cref="GroupMembership"/> → <see cref="GroupMembershipDto"/> projection.
    /// The caller supplies the already-resolved display
    /// <paramref name="userEmail"/> / <paramref name="userName"/> (original call
    /// sites all used <c>Email ?? ""</c> / <c>UserName ?? ""</c>).
    /// </summary>
    public static GroupMembershipDto ToDto(GroupMembership membership, string userEmail, string userName)
    {
        return new GroupMembershipDto
        {
            Id = membership.Id,
            GroupId = membership.GroupId,
            UserId = membership.UserId,
            UserEmail = userEmail,
            UserName = userName,
            UniqueQRCode = membership.UniqueQRCode,
            IsActive = membership.IsActive,
            JoinedAt = membership.JoinedAt,
            ExpiresAt = membership.ExpiresAt
        };
    }
}
