using RestaurantSystem.Api.Features.Groups.Dtos;

namespace RestaurantSystem.Api.Features.Groups.Interfaces;

/// <summary>
/// Membership lifecycle for user groups: add/remove members, list a group's
/// members, and render a member's QR-code image. Extracted from
/// <c>UserGroupService</c> so the facade delegates the membership concern here
/// without any behavior change.
/// </summary>
public interface IGroupMembershipService
{
    Task<GroupMembershipDto> AddMemberAsync(Guid groupId, AddMemberDto dto, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
    Task<List<GroupMembershipDto>> GetGroupMembersAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<byte[]> GetMemberQRCodeImageAsync(Guid membershipId, CancellationToken cancellationToken = default);
}
