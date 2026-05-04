using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Groups.Dtos;

public class UserGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string QRCodeData { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int MemberCount { get; set; }
    public List<GroupDiscountDto> Discounts { get; set; } = new();
}

public class CreateUserGroupDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public CreateGroupDiscountDto? InitialDiscount { get; set; }
}

public class UpdateUserGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
}

public class GroupMembershipDto
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UniqueQRCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class AddMemberDto
{
    public Guid UserId { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class GroupDiscountDto
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinimumOrderAmount { get; set; }
    public decimal? MaximumDiscountAmount { get; set; }
    public bool IsActive { get; set; }
}

public class CreateGroupDiscountDto
{
    public string Name { get; set; } = string.Empty;
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinimumOrderAmount { get; set; }
    public decimal? MaximumDiscountAmount { get; set; }
}

public class UpdateGroupDiscountDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinimumOrderAmount { get; set; }
    public decimal? MaximumDiscountAmount { get; set; }
    public bool IsActive { get; set; }
}

public class ValidateQRCodeDto
{
    public string QRCode { get; set; } = string.Empty;
}

public class QRCodeValidationResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public GroupMembershipDto? Membership { get; set; }
    public UserGroupDto? Group { get; set; }
    public List<GroupDiscountDto> ApplicableDiscounts { get; set; } = new();
}
