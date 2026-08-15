using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Groups.Dtos;
using RestaurantSystem.Api.Features.Groups.Interfaces;
using RestaurantSystem.Api.Features.Groups.Mapping;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Groups.Services;

/// <summary>
/// Membership lifecycle for user groups. Behavior is preserved verbatim from the
/// original <c>UserGroupService</c> — including the exact exception types/messages
/// and the best-effort confirmation-email send (failures are logged, never fatal
/// to membership creation).
/// </summary>
public class GroupMembershipService : IGroupMembershipService
{
    private readonly ApplicationDbContext _context;
    private readonly IQRCodeService _qrCodeService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailService _emailService;
    private readonly IEmailLanguageResolver _languages;
    private readonly ILogger<GroupMembershipService> _logger;

    public GroupMembershipService(
        ApplicationDbContext context,
        IQRCodeService qrCodeService,
        ICurrentUserService currentUserService,
        IEmailService emailService,
        IEmailLanguageResolver languages,
        ILogger<GroupMembershipService> logger)
    {
        _context = context;
        _qrCodeService = qrCodeService;
        _currentUserService = currentUserService;
        _emailService = emailService;
        _languages = languages;
        _logger = logger;
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
            // The MEMBER's language, not the staff member's who added them: this whole method
            // runs on an admin's request.
            await _emailService.SendMembershipConfirmationEmailAsync(
                _languages.ForAccount(user),
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
            // Best-effort send: a failed email must never fail membership creation, and it can
            // be resent later. What is logged is the membership and user IDs, NOT the address —
            // the recipient's email is the PII this log used to leak to stdout in plaintext, and
            // the IDs identify the same record for anyone who needs to resend (DEV-PHASES D7,
            // docs/privacy PII map). Passing the exception to ILogger carries the stack trace
            // and every inner exception, so the three hand-rolled Console lines are covered by
            // this one call rather than dropped.
            _logger.LogError(
                ex,
                "Failed to send membership confirmation email for membership {MembershipId} (user {UserId}, group {GroupId})",
                membership.Id,
                user.Id,
                group.Id);
        }

        return UserGroupMapper.ToDto(membership, user.Email ?? "", user.UserName ?? "");
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

        return memberships
            .Select(m => UserGroupMapper.ToDto(m, m.User.Email ?? "", m.User.UserName ?? ""))
            .ToList();
    }

    public async Task<byte[]> GetMemberQRCodeImageAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var membership = await _context.GroupMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken)
            ?? throw new KeyNotFoundException($"Membership with ID {membershipId} not found");

        return _qrCodeService.GenerateQRCode(membership.UniqueQRCode);
    }
}
