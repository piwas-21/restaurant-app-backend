using RestaurantSystem.Api.Features.Groups.Dtos;

namespace RestaurantSystem.Api.Features.Groups.Interfaces;

public interface IUserGroupService
{
    Task<UserGroupDto> CreateGroupAsync(CreateUserGroupDto dto, CancellationToken cancellationToken = default);
    Task<UserGroupDto> UpdateGroupAsync(UpdateUserGroupDto dto, CancellationToken cancellationToken = default);
    Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserGroupDto?> GetGroupByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<UserGroupDto>> GetAllGroupsAsync(CancellationToken cancellationToken = default);

    Task<GroupMembershipDto> AddMemberAsync(Guid groupId, AddMemberDto dto, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
    Task<List<GroupMembershipDto>> GetGroupMembersAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<byte[]> GetMemberQRCodeImageAsync(Guid membershipId, CancellationToken cancellationToken = default);

    Task<QRCodeValidationResult> ValidateMembershipByQRCodeAsync(string qrCode, CancellationToken cancellationToken = default);
    Task<decimal> CalculateDiscountAsync(Guid membershipId, decimal orderAmount, CancellationToken cancellationToken = default);
}
