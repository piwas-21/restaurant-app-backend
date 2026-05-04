using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Groups.Dtos;
using RestaurantSystem.Api.Features.Groups.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Groups.Services;

public class UserGroupService : IUserGroupService
{
    private readonly ApplicationDbContext _context;
    private readonly IQRCodeService _qrCodeService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailService _emailService;

    public UserGroupService(
        ApplicationDbContext context,
        IQRCodeService qrCodeService,
        ICurrentUserService currentUserService,
        IEmailService emailService)
    {
        _context = context;
        _qrCodeService = qrCodeService;
        _currentUserService = currentUserService;
        _emailService = emailService;
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
            Discounts = group.Discounts.Select(d => new GroupDiscountDto
            {
                Id = d.Id,
                GroupId = d.GroupId,
                Name = d.Name,
                Type = d.Type,
                Value = d.Value,
                MinimumOrderAmount = d.MinimumOrderAmount,
                MaximumDiscountAmount = d.MaximumDiscountAmount,
                IsActive = d.IsActive
            }).ToList()
        };
    }

    public async Task<List<UserGroupDto>> GetAllGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _context.UserGroups
            .Include(g => g.Discounts)
            .Include(g => g.Memberships)
            .ToListAsync(cancellationToken);

        return groups.Select(g => new UserGroupDto
        {
            Id = g.Id,
            Name = g.Name,
            Description = g.Description,
            QRCodeData = g.QRCodeData,
            IsActive = g.IsActive,
            ValidFrom = g.ValidFrom,
            ValidUntil = g.ValidUntil,
            MemberCount = g.Memberships.Count,
            Discounts = g.Discounts.Select(d => new GroupDiscountDto
            {
                Id = d.Id,
                GroupId = d.GroupId,
                Name = d.Name,
                Type = d.Type,
                Value = d.Value,
                MinimumOrderAmount = d.MinimumOrderAmount,
                MaximumDiscountAmount = d.MaximumDiscountAmount,
                IsActive = d.IsActive
            }).ToList()
        }).ToList();
    }

    public async Task<GroupMembershipDto> AddMemberAsync(Guid groupId, AddMemberDto dto, CancellationToken cancellationToken = default)
    {
        var group = await _context.UserGroups
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Group with ID {groupId} not found");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == dto.UserId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with ID {dto.UserId} not found");

        // Check if membership already exists
        var existingMembership = await _context.GroupMemberships
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == dto.UserId, cancellationToken);

        if (existingMembership != null)
        {
            throw new BadRequestException("User is already a member of this group");
        }

        // Generate unique QR code for this membership
        var qrData = $"GROUP:{groupId}:USER:{dto.UserId}:MEMBERSHIP:";
        var membershipId = Guid.NewGuid();
        qrData += membershipId.ToString();

        // Add signature
        var signature = _qrCodeService.GenerateSignature(qrData);
        var uniqueQRCode = $"{qrData}:SIG:{signature}";

        var membership = new GroupMembership
        {
            Id = membershipId,
            GroupId = groupId,
            UserId = dto.UserId,
            UniqueQRCode = uniqueQRCode,
            IsActive = true,
            JoinedAt = DateTime.UtcNow,
            ExpiresAt = dto.ExpiresAt.HasValue ? DateTime.SpecifyKind(dto.ExpiresAt.Value, DateTimeKind.Utc) : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        _context.GroupMemberships.Add(membership);
        await _context.SaveChangesAsync(cancellationToken);

        // Send confirmation email with QR code
        try
        {
            var qrCodeImage = _qrCodeService.GenerateQRCode(uniqueQRCode);
            await _emailService.SendMembershipConfirmationEmailAsync(
                user.Email!,
                $"{user.FirstName} {user.LastName}",
                group.Name,
                group.Description,
                qrCodeImage,
                uniqueQRCode,
                membership.ExpiresAt,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Log email failure but don't fail the membership creation
            // Email can be resent later if needed
            // TODO: Add proper logger injection
            Console.WriteLine($"Failed to send membership confirmation email to {user.Email}: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }

        return new GroupMembershipDto
        {
            Id = membership.Id,
            GroupId = membership.GroupId,
            UserId = membership.UserId,
            UserEmail = user.Email ?? "",
            UserName = user.UserName ?? "",
            UniqueQRCode = membership.UniqueQRCode,
            IsActive = membership.IsActive,
            JoinedAt = membership.JoinedAt,
            ExpiresAt = membership.ExpiresAt
        };
    }

    public async Task RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        var membership = await _context.GroupMemberships
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("Membership not found");

        _context.GroupMemberships.Remove(membership);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<GroupMembershipDto>> GetGroupMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var memberships = await _context.GroupMemberships
            .Include(m => m.User)
            .Where(m => m.GroupId == groupId)
            .ToListAsync(cancellationToken);

        return memberships.Select(m => new GroupMembershipDto
        {
            Id = m.Id,
            GroupId = m.GroupId,
            UserId = m.UserId,
            UserEmail = m.User.Email ?? "",
            UserName = m.User.UserName ?? "",
            UniqueQRCode = m.UniqueQRCode,
            IsActive = m.IsActive,
            JoinedAt = m.JoinedAt,
            ExpiresAt = m.ExpiresAt
        }).ToList();
    }

    public async Task<byte[]> GetMemberQRCodeImageAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var membership = await _context.GroupMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken)
            ?? throw new KeyNotFoundException($"Membership with ID {membershipId} not found");

        return _qrCodeService.GenerateQRCode(membership.UniqueQRCode);
    }

    public async Task<QRCodeValidationResult> ValidateMembershipByQRCodeAsync(string qrCode, CancellationToken cancellationToken = default)
    {
        try
        {
            // Parse QR code format: GROUP:{groupId}:USER:{userId}:MEMBERSHIP:{membershipId}:SIG:{signature}
            var parts = qrCode.Split(':');
            if (parts.Length != 8 || parts[0] != "GROUP" || parts[2] != "USER" || parts[4] != "MEMBERSHIP" || parts[6] != "SIG")
            {
                return new QRCodeValidationResult
                {
                    IsValid = false,
                    Message = "Invalid QR code format"
                };
            }

            var groupId = Guid.Parse(parts[1]);
            var userId = Guid.Parse(parts[3]);
            var membershipId = Guid.Parse(parts[5]);
            var signature = parts[7];

            // Validate signature
            var dataToValidate = $"GROUP:{groupId}:USER:{userId}:MEMBERSHIP:{membershipId}";
            if (!_qrCodeService.ValidateSignature(dataToValidate, signature))
            {
                return new QRCodeValidationResult
                {
                    IsValid = false,
                    Message = "Invalid QR code signature"
                };
            }

            // Get membership
            var membership = await _context.GroupMemberships
                .Include(m => m.Group)
                    .ThenInclude(g => g.Discounts)
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken);

            if (membership == null)
            {
                return new QRCodeValidationResult
                {
                    IsValid = false,
                    Message = "Membership not found"
                };
            }

            // Check if membership is active
            if (!membership.IsActive)
            {
                return new QRCodeValidationResult
                {
                    IsValid = false,
                    Message = "Membership is inactive"
                };
            }

            // Check if membership has expired
            if (membership.ExpiresAt.HasValue && membership.ExpiresAt.Value < DateTime.UtcNow)
            {
                return new QRCodeValidationResult
                {
                    IsValid = false,
                    Message = "Membership has expired"
                };
            }

            // Check if group is active
            if (!membership.Group.IsActive)
            {
                return new QRCodeValidationResult
                {
                    IsValid = false,
                    Message = "Group is inactive"
                };
            }

            // Check group validity period
            var now = DateTime.UtcNow;
            if (membership.Group.ValidFrom.HasValue && membership.Group.ValidFrom.Value > now)
            {
                return new QRCodeValidationResult
                {
                    IsValid = false,
                    Message = "Group is not yet valid"
                };
            }

            if (membership.Group.ValidUntil.HasValue && membership.Group.ValidUntil.Value < now)
            {
                return new QRCodeValidationResult
                {
                    IsValid = false,
                    Message = "Group validity has expired"
                };
            }

            // Get applicable discounts
            var applicableDiscounts = membership.Group.Discounts
                .Where(d => d.IsActive)
                .Select(d => new GroupDiscountDto
                {
                    Id = d.Id,
                    GroupId = d.GroupId,
                    Name = d.Name,
                    Type = d.Type,
                    Value = d.Value,
                    MinimumOrderAmount = d.MinimumOrderAmount,
                    MaximumDiscountAmount = d.MaximumDiscountAmount,
                    IsActive = d.IsActive
                }).ToList();

            return new QRCodeValidationResult
            {
                IsValid = true,
                Message = "Valid membership",
                Membership = new GroupMembershipDto
                {
                    Id = membership.Id,
                    GroupId = membership.GroupId,
                    UserId = membership.UserId,
                    UserEmail = membership.User.Email ?? "",
                    UserName = membership.User.UserName ?? "",
                    UniqueQRCode = membership.UniqueQRCode,
                    IsActive = membership.IsActive,
                    JoinedAt = membership.JoinedAt,
                    ExpiresAt = membership.ExpiresAt
                },
                Group = new UserGroupDto
                {
                    Id = membership.Group.Id,
                    Name = membership.Group.Name,
                    Description = membership.Group.Description,
                    QRCodeData = membership.Group.QRCodeData,
                    IsActive = membership.Group.IsActive,
                    ValidFrom = membership.Group.ValidFrom,
                    ValidUntil = membership.Group.ValidUntil,
                    MemberCount = 0,
                    Discounts = applicableDiscounts
                },
                ApplicableDiscounts = applicableDiscounts
            };
        }
        catch (Exception ex)
        {
            return new QRCodeValidationResult
            {
                IsValid = false,
                Message = $"Error validating QR code: {ex.Message}"
            };
        }
    }

    public async Task<decimal> CalculateDiscountAsync(Guid membershipId, decimal orderAmount, CancellationToken cancellationToken = default)
    {
        var membership = await _context.GroupMemberships
            .Include(m => m.Group)
                .ThenInclude(g => g.Discounts)
            .FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken)
            ?? throw new KeyNotFoundException($"Membership with ID {membershipId} not found");

        var applicableDiscounts = membership.Group.Discounts
            .Where(d => d.IsActive)
            .Where(d => !d.MinimumOrderAmount.HasValue || orderAmount >= d.MinimumOrderAmount.Value)
            .ToList();

        if (!applicableDiscounts.Any())
        {
            return 0;
        }

        // Apply the best discount (highest value)
        decimal maxDiscount = 0;
        foreach (var discount in applicableDiscounts)
        {
            decimal discountAmount = discount.Type == DiscountType.Percentage
                ? orderAmount * (discount.Value / 100)
                : discount.Value;

            // Apply maximum discount cap if set
            if (discount.MaximumDiscountAmount.HasValue && discountAmount > discount.MaximumDiscountAmount.Value)
            {
                discountAmount = discount.MaximumDiscountAmount.Value;
            }

            if (discountAmount > maxDiscount)
            {
                maxDiscount = discountAmount;
            }
        }

        return maxDiscount;
    }
}
