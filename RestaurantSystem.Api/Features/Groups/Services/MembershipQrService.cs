using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Groups.Dtos;
using RestaurantSystem.Api.Features.Groups.Interfaces;
using RestaurantSystem.Api.Features.Groups.Mapping;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Groups.Services;

/// <summary>
/// QR-code validation + discount calculation for group memberships. Extracted
/// verbatim from the original <c>UserGroupService</c>; the parsing/signature
/// checks, the ordered validity state machine and its exact messages, and the
/// best-discount selection (with min-order filter and max cap) are unchanged.
/// </summary>
public class MembershipQrService : IMembershipQrService
{
    private readonly ApplicationDbContext _context;
    private readonly IQRCodeService _qrCodeService;

    public MembershipQrService(ApplicationDbContext context, IQRCodeService qrCodeService)
    {
        _context = context;
        _qrCodeService = qrCodeService;
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
                .Select(UserGroupMapper.ToDto)
                .ToList();

            return new QRCodeValidationResult
            {
                IsValid = true,
                Message = "Valid membership",
                Membership = UserGroupMapper.ToDto(membership, membership.User.Email ?? "", membership.User.UserName ?? ""),
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

            // Apply the maximum cap only when one is actually set. A stored 0 means
            // "no cap", not "cap everything to nothing" — same rule, same reason and
            // the same guard as CustomerDiscountService.CalculateGroupDiscountAmount,
            // which reads THIS VERY COLUMN on the same rows for the basket path. The 0
            // is reachable: the admin discount form coerces the API's null (and an
            // emptied input) to 0 on every save, so an untouched uncapped discount is
            // rewritten to a cap of zero. Without the > 0 the discount stops discounting.
            if (discount.MaximumDiscountAmount.HasValue &&
                discount.MaximumDiscountAmount.Value > 0 &&
                discountAmount > discount.MaximumDiscountAmount.Value)
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
