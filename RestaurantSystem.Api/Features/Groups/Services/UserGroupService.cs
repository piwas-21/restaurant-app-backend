using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Groups.Dtos;
using RestaurantSystem.Api.Features.Groups.Interfaces;
using RestaurantSystem.Api.Features.Groups.Mapping;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Groups.Services;

/// <summary>
/// Facade over the user-group feature: owns group CRUD directly and delegates the
/// membership lifecycle to <see cref="IGroupMembershipService"/> and the
/// QR-validation / discount-calculation paths to <see cref="IMembershipQrService"/>.
/// The full <see cref="IUserGroupService"/> surface (and therefore the controller
/// contract) is unchanged; the split is behavior-preserving.
/// </summary>
public class UserGroupService : IUserGroupService
{
    private readonly ApplicationDbContext _context;
    private readonly IQRCodeService _qrCodeService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IGroupMembershipService _groupMembershipService;
    private readonly IMembershipQrService _membershipQrService;

    public UserGroupService(
        ApplicationDbContext context,
        IQRCodeService qrCodeService,
        ICurrentUserService currentUserService,
        IGroupMembershipService groupMembershipService,
        IMembershipQrService membershipQrService)
    {
        _context = context;
        _qrCodeService = qrCodeService;
        _currentUserService = currentUserService;
        _groupMembershipService = groupMembershipService;
        _membershipQrService = membershipQrService;
    }

    public async Task<UserGroupDto> CreateGroupAsync(CreateUserGroupDto dto, CancellationToken cancellationToken = default)
    {
        var group = new UserGroup
        {
            Name = dto.Name,
            Description = dto.Description,
            QRCodeData = _qrCodeService.GenerateUniqueCode(),
            IsActive = true,
            ValidFrom = dto.ValidFrom.HasValue ? DateTime.SpecifyKind(dto.ValidFrom.Value, DateTimeKind.Utc) : null,
            ValidUntil = dto.ValidUntil.HasValue ? DateTime.SpecifyKind(dto.ValidUntil.Value, DateTimeKind.Utc) : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        // Add initial discount if provided
        if (dto.InitialDiscount != null)
        {
            var discount = new GroupDiscount
            {
                Name = dto.InitialDiscount.Name,
                Type = dto.InitialDiscount.Type,
                Value = dto.InitialDiscount.Value,
                MinimumOrderAmount = dto.InitialDiscount.MinimumOrderAmount,
                MaximumDiscountAmount = dto.InitialDiscount.MaximumDiscountAmount,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };
            group.Discounts.Add(discount);
        }

        _context.UserGroups.Add(group);

        await _context.SaveChangesAsync(cancellationToken);

        return await GetGroupByIdAsync(group.Id, cancellationToken)
            ?? throw new BadRequestException("Failed to retrieve created group");
    }

    public async Task<UserGroupDto> UpdateGroupAsync(UpdateUserGroupDto dto, CancellationToken cancellationToken = default)
    {
        var group = await _context.UserGroups
            .FirstOrDefaultAsync(g => g.Id == dto.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Group with ID {dto.Id} not found");

        group.Name = dto.Name;
        group.Description = dto.Description;
        group.IsActive = dto.IsActive;
        group.ValidFrom = dto.ValidFrom.HasValue ? DateTime.SpecifyKind(dto.ValidFrom.Value, DateTimeKind.Utc) : null;
        group.ValidUntil = dto.ValidUntil.HasValue ? DateTime.SpecifyKind(dto.ValidUntil.Value, DateTimeKind.Utc) : null;
        group.UpdatedAt = DateTime.UtcNow;
        group.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        return await GetGroupByIdAsync(group.Id, cancellationToken)
            ?? throw new BadRequestException("Failed to retrieve updated group");
    }

    public async Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var group = await _context.UserGroups
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Group with ID {id} not found");

        _context.UserGroups.Remove(group);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserGroupDto?> GetGroupByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var group = await _context.UserGroups
            .Include(g => g.Discounts)
            .Include(g => g.Memberships)
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

        if (group == null) return null;

        return UserGroupMapper.ToDto(group);
    }

    public async Task<List<UserGroupDto>> GetAllGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _context.UserGroups
            .Include(g => g.Discounts)
            .Include(g => g.Memberships)
            // Discounts x Memberships per group, over every group (S8733). A group with 200
            // members and 5 discounts is 1000 rows for 205 entities. Unpaginated, so no
            // ordering guarantee is needed. GetGroupByIdAsync above is deliberately NOT
            // split: one root, and the extra round-trip costs more than the duplication.
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return groups.Select(UserGroupMapper.ToDto).ToList();
    }

    public Task<GroupMembershipDto> AddMemberAsync(Guid groupId, AddMemberDto dto, CancellationToken cancellationToken = default)
        => _groupMembershipService.AddMemberAsync(groupId, dto, cancellationToken);

    public Task RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
        => _groupMembershipService.RemoveMemberAsync(groupId, userId, cancellationToken);

    public Task<List<GroupMembershipDto>> GetGroupMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
        => _groupMembershipService.GetGroupMembersAsync(groupId, cancellationToken);

    public Task<byte[]> GetMemberQRCodeImageAsync(Guid membershipId, CancellationToken cancellationToken = default)
        => _groupMembershipService.GetMemberQRCodeImageAsync(membershipId, cancellationToken);

    public Task<QRCodeValidationResult> ValidateMembershipByQRCodeAsync(string qrCode, CancellationToken cancellationToken = default)
        => _membershipQrService.ValidateMembershipByQRCodeAsync(qrCode, cancellationToken);

    public Task<decimal> CalculateDiscountAsync(Guid membershipId, decimal orderAmount, CancellationToken cancellationToken = default)
        => _membershipQrService.CalculateDiscountAsync(membershipId, orderAmount, cancellationToken);
}
